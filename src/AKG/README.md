# AKG — Agent Knowledge Graph

Der **AKG** ist das Langzeit-Gedächtnis und Wissens-Backbone des Agents. Er speichert Wissensregeln in einem Neo4j-Graphen und kompiliert bei jedem Turn einen relevanten Kontext für die Agent-Pipeline.

Kernkonzept: **Wissen wird als Markdown-Dateien verwaltet**, vom KnowledgeCompiler geparst, und als Nodes/Relationships im Neo4j-Graph gespeichert.

---

## Abhängigkeiten

```
AKG → Core  (NICHT Agent)
```

Externe Pakete: `Neo4j.Driver`, `Microsoft.Extensions.*`

---

## Verzeichnisstruktur

```
AKG/
├── Confidence/       ← Konfidenz-Scores pro Regel (Sliding Window)
├── Context/          ← ContextCompiler: Regel-Selektion für einen Turn
├── DependencyInjection/
├── Embeddings/       ← Neo4j-Embedding-Cache
├── Feedback/         ← AKG-Feedback-Loop (TDK → AKG)
├── Graph/            ← Neo4j-Zugriff, Domain-Manager, Graph-Validierung
└── Parser/           ← Markdown → KnowledgeRule
```

---

## Graph/

### `Neo4jKnowledgeGraph.cs`
Implementiert `IKnowledgeGraph`. Direkter Neo4j-Treiber-Zugriff via `ICypherExecutor`.

Operationen:
- `GetRulesAsync(userId?)` — alle aktiven Regeln, optional user-scoped
- `StoreRuleAsync(rule)` — Regel als Node + Relationships speichern
- `DeleteRuleAsync(ruleId)` — Regel und alle Kanten entfernen
- `GetConflictsAsync()` — `CONFLICTS_WITH`-Kanten abfragen
- `SearchByKeywordsAsync(keywords)` — Keyword-basierte Suche

Alle Queries folgen `docs/06_neo4j-schema.md`. **Kein Cypher ohne dieses Dokument schreiben.**

### `Neo4jCypherExecutor.cs` / `ICypherExecutor.cs`
Thin Wrapper um `IAsyncSession.RunAsync(...)`. Abstrahiert den Neo4j-Driver für Testbarkeit.

### `DomainManager.cs`
Implementiert `IDomainManager`. Verwaltet Domain-Hierarchien im Graphen:
- `CreateDomainAsync(name, label, parentDomain?)`
- `GetDomainHierarchyAsync()`
- Domains sind Nodes mit `HAS_SUBDOMAIN`-Kanten

### `GraphValidator.cs`
Validiert Graph-Integrität: prüft auf Konflikt-Zyklen, orphaned Nodes, fehlende Pflicht-Properties.

### `NodeMapper.cs`
Konvertiert Neo4j-`IRecord`-Ergebnisse in `KnowledgeRule`-DTOs.

### `RuleLoader.cs`
Lädt alle Regeln aus einer Markdown-Datei via `KnowledgeRuleParser` und speichert sie im Graphen.

### `WorldKnowledgeSeeder.cs` / `WorldKnowledgeSeedHostedService.cs`
Seeded beim ersten Start Basis-Weltwissen aus `knowledge/`-Verzeichnis. Läuft einmalig beim App-Start.

---

## Parser/

### `KnowledgeRuleParser.cs`
Parst Markdown-Dateien mit spezieller Syntax in `KnowledgeRule`-Objekte:

```markdown
## Regelname {#rule-id}
> Priorität: high | medium | low
> Tags: tag1, tag2

Regeltext hier...

### Beispiele
- Positiv: ...
- Negativ: ...
```

Unterstützte Regeltypen: `Fact`, `Constraint`, `Procedure`, `Relation`, `Concept`.

---

## Context/

Der **ContextCompiler** ist die wichtigste Klasse des AKG. Er selektiert bei jedem Turn die relevantesten Regeln.

### 4-Phasen-Compilation:

1. **Keyword-Scoring** (`KeywordScorer`) — BM25-ähnliches Scoring anhand von Übereinstimmungen zwischen Anfrage-Keywords und Regel-Tags
2. **Semantic Boosting** (`SemanticBooster`) — Embedding-Cosine-Similarity boosted semantisch verwandte Regeln
3. **MMR-Reranking** (`RuleMmrReranker`) — diversifiziert die Auswahl, um Redundanz zu vermeiden
4. **Graph-Expansion** (`GraphExpander`) — traversiert `RELATED_TO`-Kanten im Graphen für transitive Relevanz und löst Konflikte auf

### `ScoredRule.cs`
Intermediate-Typ: `KnowledgeRule` + `double Score` für Ranking.

### `WorldKnowledgeFetcher.cs`
Lädt generelles Weltwissen (nicht user-spezifisch) als Kontext-Hintergrund.

---

## Confidence/

### `RuleConfidenceStore.cs` / `SlidingWindowRuleConfidenceStore.cs`
Implementiert `IRuleConfidenceStore`. Speichert Konfidenz-Scores pro Regel als Sliding-Window-Durchschnitt der letzten N Validierungen. Niedrige Konfidenz → Regel wird bei Compilation de-priorisiert.

---

## Embeddings/

### `Neo4jEmbeddingCache.cs`
Cacht Embedding-Vektoren direkt als Properties auf Neo4j-Nodes. Verhindert wiederholte API-Aufrufe beim Semantic Boosting.

---

## Feedback/

AKG-Feedback-Loop: TDK-Violations und User-Feedback fließen als Konfidenz-Anpassungen zurück in den Graphen.

### `RuleFeedbackService.cs`
Implementiert `IRuleFeedbackService`. Nimmt Feedback entgegen (TDK-Verletzung, User-Rating, Compliance-Check) und delegiert an `ConfidenceAdjuster`.

### `ConfidenceAdjuster.cs`
Passt Konfidenz-Scores an: Positiv-Feedback erhöht, Negativ-Feedback senkt den Score.

### `RuleFeedbackStore.cs`
Persistiert Feedback-Einträge in Neo4j als `FEEDBACK`-Relationships.

### `FeedbackSummaryJob.cs`
`BackgroundService`. Aggregiert Feedback täglich und schreibt konsolidierte Konfidenz-Updates.

---

## Umgebungsvariablen

| Variable | Beschreibung |
|---|---|
| `NEO4J_URI` | Neo4j Bolt-URI (z.B. `bolt://localhost:7687`) |
| `NEO4J_USERNAME` | Neo4j-Benutzer |
| `NEO4J_PASSWORD` | Neo4j-Passwort |
| `KNOWLEDGE_DIR` | Pfad zum Wissens-Verzeichnis (Standard: `knowledge/`) |

---

## Wichtige Regeln

- **Kein Cypher ohne `docs/06_neo4j-schema.md`** zu lesen
- AKG referenziert nur Core, nie Agent
- User-Scoping: jede Query muss `userId` einschließen
