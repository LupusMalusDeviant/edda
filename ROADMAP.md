# Edda — Roadmap: Aufschließen zu Cognee

> Strategische Roadmap für Edda (lokal-only Wissensgraph + TDK über MCP). Ziel ist
> nicht, die Breite eines finanzierten Teams (Cognee) nachzubauen, sondern die
> entscheidenden Lücken zu schließen und Eddas verteidigbare Stärken auszubauen.

## Ausgangslage

Im Vergleich mit dem finanzierten OSS-Memory-Framework Cognee (GraphRAG) liegt Edda
heute in vier Punkten zurück: automatischer Wissensgraph-Aufbau aus Rohdaten,
episodisches Agent-Gedächtnis, Mandantenfähigkeit und Ökosystem-Breite. Dafür hat
Edda echte Alleinstellungsmerkmale: **Safety-First-MCP** (read-only, default-deny),
**TDK** (Wissen validiert Code aktiv), ein **Security-/Compliance-Layer**
(HMAC/Merkle-Audit, Redaction, Taint, AES-GCM), **.NET-nativ** und **kein großes LLM
nötig** (kuratiertes Wissen, robust auf schwacher Hardware).

## Leitprinzip

3–4 Kern-Lücken schließen, die einzigartigen Trümpfe laut ausspielen, die unrealistischen
Felder bewusst auslassen. Zielkorridor: Gesamtscore von ~40 auf ~47–48/60; Kategoriesiege
im privaten/lokalen .NET-Umfeld und beim sicherheitskritischen Agent-Zugriff.

---

## Track 0 — Quick Wins: Fundament & Onboarding  *(Meilenstein M1)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 0.1 | **Zero-Infra-Dev-Modus** `GRAPH_PROVIDER=memory` ✅ | hoch | L (3 Sessions) | neuer `MemoryGraphDatabaseProvider` + `InMemoryCypherExecutor` (String-Dispatch der ~40 Cypher-Shapes); DI-Switch in `AddAkgServices` | **✅ Vollständig (Stage 1–3)** — `GRAPH_PROVIDER=memory dotnet run` bootet ohne Neo4j; **Stats + Context-Compile live verifiziert** (`/api/akg/stats` + `/api/akg/context` → HTTP 200). ~40 Cypher-Shapes in-memory; Embeddings/Semantik sind bewusste No-ops (Keyword-Retrieval, kein Vektor-Backend). Gesamt-Suite 940 Tests grün |
| 0.2 | **Confidence-Decay / Vergessenskurve** ✅ | mittel | S | `ConfidenceAdjuster`, `RuleFeedbackService` | **Umgesetzt:** Multiplier fällt zeitbasiert Richtung neutral (`FEEDBACK_DECAY_HALFLIFE_DAYS`, Default 90). Stale-Liste via `analyze_coverage` (0.3) ✅ |
| 0.3 | **Proaktive Gap-Analyse** (read-only MCP-Tool `analyze_coverage`) ✅ | mittel-hoch | M | `Agent/Tools/Knowledge`, `IRuleFeedbackService` | **Umgesetzt:** meldet dünne Domains, kaputte Referenzen, offene Konflikte, Low-Confidence & veraltete ("stale") Regeln; opt-in über `MCP_EXPOSED_TOOLS`, default-deny bleibt |
| 0.4 | **Produktnarrativ** (README „für wen / wofür") ✅ | niedrig | S | `README.md` | **Umgesetzt:** „Für wen & wofür"-Abschnitt (privat / Team / Standards-Wächter; Safety-MCP; kein großes LLM) |

## Track 1 — Auto-Wissensgraph aus Rohdaten  *(größter Hebel, M2)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 1.1 | **Optionale LLM-Extraktion** von Entitäten/Relationen beim Ingest | hoch | L | `LlmIngestionEnricher` (in voller Pipeline vorhanden) verdrahten | aus rohem Text/Doku entstehen typisierte Knoten+Kanten; abschaltbar; keine erfundenen Knoten |
| 1.2 | **Quell-Connectoren** pragmatisch (Dateisystem/Git/Markdown/PDF zuerst) | mittel | M | `AKG.Ingestion` / `IIngestionSource` | ≥3 Connectoren produktiv |
| 1.3 | **Entity-Layer-Retrieval** ausbauen (LightRAG-Stil ist angelegt) | mittel | M | `ContextCompiler.BuildEntityContextAsync` | Entitäts-Nachbarschaft fließt in den Kontext |

**M2-Umsetzungsstand (2026-07-01):** Der LLM-Ingestion-Enricher **und** die Quell-Connectoren
waren bereits aus dem Monorepo vorhanden und in `Edda.Hosting` verdrahtet (Default AUS,
provider-pluggable) — die Plan-Annahme „dormant/nicht verdrahtet" traf nicht zu. **Geschlossen:**
die einzige echte Sicherheitslücke in 1.1 — Sanitization/Redaction **vor jedem LLM-Call**
(FR-07/Risiko R6): `LlmIngestionEnricher` wendet nun `ISecretRedactor` + `IInputSanitizer` auf
Titel und Body an; Unit-Tests ohne echten LLM grün. **Offen (im Loop):** Default-Provider
(Ollama/openrouter bleiben beide first-class, per Config), Verifikation Entity-Layer-Feed (1.3)
+ Idempotenz/Bulk (1.2), Betriebs-/Datenschutz-Doku. Reale Extraktionsqualität ist nur mit
lokalem Ollama abnehmbar (M4).

**WP3-Fortschritt (2026-07-01):** Der Entity-Layer war halb gebaut — Retrieval (`BuildEntityContextAsync`,
F49) vorhanden, aber die Schreib-Seite fehlte ganz (`IEntityExtractor`/`IEntityIngestionService` waren
leere Interfaces, der Store wurde nie befüllt). **Scheibe A umgesetzt:** `LlmEntityExtractor` (LightRAG-Stil,
best-effort, Sanitization/Redaction vor dem LLM, verwirft Relationen zu unbekannten Entitäten) +
`EntityIngestionService` + opt-in Admin-Endpoint `POST /api/akg/entities/ingest` (gated via
`ENABLE_INGESTION`, userId aus Identity/Regel 6) + DI-Registrierung; Tests mit Mock-LLM grün. Kehrt die
bewusste „entity ingestion omitted"-Auslassung opt-in um (ADR-0010). **Scheibe B umgesetzt:** Auto-Extraktion
in der Ingestion-Pipeline als opt-in Stufe (`INGESTION_ENTITY_EXTRACTION=true`, getrennt vom Enricher,
Default AUS); pro Item wird der Rohtext extrahiert und user-scoped (Identity/„local") in den Entity-Layer
persistiert; Pipeline-Tests grün.

**WP2 verifiziert (2026-07-01):** Idempotenz ist bereits garantiert — deterministische Rule-IDs +
`MERGE (r:Rule {id})`-Upsert → Re-Ingest erzeugt keine Duplikate (FR-08-MUST erfüllt); `BeginBulkIngestion`
vorhanden. Ein Content-Hash-*Skip* (Re-Embedding Unveränderter vermeiden) ist nur optionale Optimierung,
kein MUST-Gap. **WP5 umgesetzt (2026-07-01):** `docs/betrieb.md` (Ollama-Setup + Aktivierung + Datenschutz),
ADR-0010 → Akzeptiert (beide Provider first-class), `CLAUDE.md` präzisiert (Ingest-LLM ist opt-in, kein
Chat-Loop), `.env.example`-Toggles. **→ M2 ist damit funktional & mock-getestet abgeschlossen; die einzige
offene Position ist die reale Extraktionsqualität, nur mit lokalem Ollama abnehmbar (M4).**

## Track 2 — Episodisches Agent-Gedächtnis  *(M2 → M3)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 2.1 | **Sitzungs-Erfassung + Konsolidierung** ins Langzeit-Graph | hoch | L | Agent-Layer, `AddTdkEngine`/Tool-Layer | Konversations-Fakten landen kuratiert im Graph |
| 2.2 | **`remember` / `recall` / `forget`** als abgesicherte Tools | hoch | M | Agent `ToolRegistry`, MCP-Exposure | persistierbar/abrufbar/vergessbar; über MCP weiterhin default-deny |
| 2.3 | **Vergessens-Policy** (Decay aus 0.2 integrieren) | mittel | S | `RuleFeedbackService` | Decay steuert Konsolidierung/Forget |

**M3-Fortschritt — episodisches Gedächtnis (2026-07-01):** Audit: die 5 vorhandenen memory-Tools sind NICHT
episodisch (`manage_memory`=Datei, `manage_learnings`=1 Aggregat-Knoten/User, `search_memory`=global).
**WP4 umgesetzt (2.2):** `remember`/`recall`/`forget` als eigenständige Tools über pro-Fakt-Knoten
(`SourceType=memory`, `OwnerId=userId`, Content-Hash-ID → idempotent) auf bestehendem `IKnowledgeGraph`;
`recall` = user-/memory-gefilterter Abruf + Keyword-Ranking (kein neues Retrieval-System, ADR-0011). MCP:
`remember`/`forget` in der Write-default-deny-Liste, `recall` opt-in (nicht in den Defaults). Tests grün
(Mock-Graph). **WP5.2 (Decay, 2.3) umgesetzt:** `recall` gewichtet Treffer mit einer Vergessenskurve
(`RecencyFactor`, Halbwertszeit 90 Tage): veraltete Erinnerungen sinken im Ranking, erneutes `remember`
frischt eine Erinnerung auf. **WP5.1 (Konsolidierung) umgesetzt:** opt-in `consolidate_memory`-Tool
(deterministisch, MCP-default-deny) entfernt normalisierte Duplikate (Groß/Klein/Whitespace, behält das
neueste) und löscht stark verblasste Erinnerungen (Recency ≤ 0,05). **→ Episodisches Gedächtnis
(Track 2 / M3-Teil 1) funktional & voll test-abgedeckt abgeschlossen — kein LLM nötig.** Nächster
M3-Block: Mandantenfähigkeit (Track 3) — davor Pause zur Scope-Bestätigung.

## Track 3 — Mandantenfähigkeit  *(M3)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 3.1 | **Tenants/Organisationen + Rollen** (Owner/Editor/Viewer) | hoch | L | `IIdentityContext`, Hosting-Auth | Org-Ebene zusätzlich zum User-Scoping |
| 3.2 | **Dataset-/Domain-Permissions** (read/write/share) | mittel | M | Graph-Provider, Auth | vollständige Daten-Isolation pro Tenant; getestet |

**M3-Fortschritt — Mandantenfähigkeit (2026-07-01):** Nutzer-Entscheidung: **Graph-Store-Isolation zuerst**
(Rest — Entity-/Feedback-Store-Filter, Dataset-Permissions, Admin-API — bewusst vertagt). **Scheibe A
umgesetzt (additiv, non-regression):** `KnowledgeRule.TenantId` (Default `"default"` → Bestand migriert
automatisch) wird in Neo4j-Cypher, `NodeMapper` und Frontmatter-Serializer/Parser persistiert; Core-Konstante
`Tenants.DefaultTenantId` (das Rollen-Enum folgt mit dem Enforcement). **Noch kein Filter** → single-user verhält sich identisch.
ADR-0012 akzeptiert. **Offen:** Scheibe B = Tenant-Filter in den Graph-Queries + Isolationstests (davor
Rückfrage zum Threading-Ansatz).

## Track 4 — Backend-Flexibilität  *(M2)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 4.1 | **Eingebetteter Graph** (Kuzu-.NET oder SQLite-Graph) als Default-Option | hoch | M | Graph-Provider-Abstraktion (deckt zugleich 0.1) | Neo4j wird optional |
| 4.2 | **Vektor-Store entkoppeln** (Provider-Interface) | mittel | M | Embeddings/AKG | Backend per Config wechselbar |

**Backend-Flexibilität — Fortschritt (2026-07-04):** Nutzer-Ziel: Edda als modulares, provider-steckbares
Framework (SAP-Spirit). **ADR-0013 akzeptiert** — semantische Persistenz-Naht `IGraphStore` (Intent statt
Cypher-Strings) + `IVectorStore` (Embedding-Entkopplung), damit nicht-Cypher-Backends (SQLite/Kuzu/Postgres,
Track 4.1) und austauschbare Vektor-Stores (4.2) andocken. **Umsetzung in kleinen, verhaltensneutralen
Scheiben:** *Scheiben 1a+1b umgesetzt.* **1a (READ):** `IGraphStore` (5 Lese-Ops) + `CypherGraphStore` (baut das
bisherige Cypher, führt es über `ICypherExecutor` aus → deckt Neo4j/Memgraph **und** den In-Memory-Dev-Executor
ab; Tenant ambient via `IIdentityContext`, C1). **1b (WRITE, Nutzer-Entscheidung „reine Graph-Writes"):**
UpsertRuleGraph (MERGE + C9-Kanten), Soft-Delete (E10), Subtree-Delete und Supersede-Invalidierung als reine
Cypher-Primitive im Store (`TimeProvider` injiziert); Auth-Gate (C2), Embedding und Bulk-Ingestion bleiben im
Orchestrator — ein nicht-Cypher-Backend muss so nur Cypher-Äquivalente liefern, keine Policy/kein Embedding.
`Neo4jKnowledgeGraph` delegiert Reads **und** Writes. **Scheibe 2 (Context-Compile):** der Kandidaten-Fetch
(`LoadRules` — Scope/Tool-Gating/Prefix-Pruning/temporal) und die 1-Hop-Nachbar-Traversierung (`Expand`) liegen
jetzt hinter `IGraphStore` (`GetCompilationRulesAsync`/`FindOpenNeighborsAsync`); `ContextCompiler` und
`GraphExpander` behalten Toolbox-Resolution, BFS-Schleife und Scoring/MMR. DI registriert
`IGraphStore→CypherGraphStore`. Cypher byte-identisch verschoben → alle Bestandstests grün, +16
Store-Unit-Tests. **Offen:** Scheibe 3 (Entity/Stats/Domains/Seeding), Scheibe 4 (`IVectorStore` —
Embeddings/Semantic-Search/Head-Vektoren; deckt zugleich SemanticBooster + WorldKnowledge/Domain-Reads).

## Track 5 — Moat ausbauen: Differenzierung  *(laufend)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 5.1 | **Safety-First-MCP** schärfen & dokumentieren | hoch | S | `AKG.Mcp` `McpExposurePolicy` | „fremden Agenten gefahrlos exponierbar" belegt |
| 5.2 | **TDK produktisieren** (Validator-Bibliothek für gängige Standards) | hoch | M | `Agent/Tdk`, `Sandboxing` | mitgelieferte Validatoren; als Coding-Standards-Wächter positioniert |
| 5.3 | **Compliance-Paket** sichtbar machen (Audit/Redaction/Taint) | mittel | S | `Security` | Zielgruppe reguliert dokumentiert |

---

## Sequenzierung (illustrative Score-Wirkung, von 60)

- **M1** (Track 0): 36 → ~40,5 — gewinnt Privat · Code.
- **M2** (Track 1 + 4 + Start Track 2): ~40,5 → ~44 — Agent-Kategorien steigen.
- **M3** (Track 2 fertig + Track 3): ~44 → ~47–48 — kippt den Gesamtvergleich gegenüber
  Cognee plausibel; SurrealDB bleibt nur bei Firma · Projektmanagement vorn.

## Bewusst NICHT verfolgt (unrealistisch solo / kein Hebel)

- 30+ Connectoren, 16+ Search-Types und Framework-Integrationen in voller Breite.
- Eigenes Cloud-/SaaS-Angebot; community-/funding-getriebene Reichweite.
- Projektmanagement-Produktfunktionen — Firma · PM bleibt SurrealDB-Domäne.

## Erfolgskriterien

- `dotnet run` ohne Docker.
- Auto-Wissensgraph aus mindestens drei Quellen, abschaltbar.
- `remember`/`recall`/`forget` produktiv, MCP weiterhin read-only per Default.
- Mandanten-Isolation getestet.
- Alleinstellungsmerkmale dokumentiert und demonstrierbar.
