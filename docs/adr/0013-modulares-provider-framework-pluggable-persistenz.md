# ADR-0013: Modulares Provider-Framework mit pluggable Persistenz (IGraphStore + IVectorStore)

- **Status:** Akzeptiert
- **Datum:** 2026-07-04
- **Autor:** LupusMalusDeviant
- **Konsultiert:** —

## Kontext und Problemstellung

Edda soll sich von einer fest Neo4j-/Cypher-gebundenen Wissensgraph-App zu einem **modularen,
provider-steckbaren Framework** entwickeln ("SAP-Spirit": vorgefertigte Module, jedes = Interface +
Default-Implementierung + Config, pro Deployment an-/abschaltbar). Die Basis ist da — alles ist
interface-first (ADR-0003), DI-registriert und config-getrieben —, aber die **Persistenz** ist die letzte
harte Kopplung: die Graph-Schicht baut Cypher-Strings und setzt ein Cypher-sprechendes Backend voraus.

Bestehende Nähte: `IGraphDatabaseProvider` (wählt/erzeugt den Executor), `ICypherExecutor` (führt Cypher
aus; Neo4j-Treiber **oder** `InMemoryCypherExecutor`), `IEmbeddingService` (sechs Provider). Damit sind
**Cypher-Backends** (Neo4j/Memgraph/FalkorDB) bereits steckbar — aber ein **nicht-Cypher-Backend**
(SQLite/Postgres/Kuzu) kann nicht andocken, und die **Vektoren** leben im Graph (keine unabhängige
Skalierung des Retrievals).

**Kernfrage:** Über welche Naht wird die Persistenz so gekapselt, dass auch nicht-Cypher-Backends und ein
entkoppelter Vektor-Store als Module andocken — ohne die bestehende, gut getestete Cypher-Schicht zu
brechen?

## Anforderungen

### Funktional

- Ein Backend-Wechsel (Neo4j ↔ Memgraph ↔ embedded/SQL) erfolgt über Config, ohne Änderung der
  Wissensgraph-API.
- Der Vektor-/Retrieval-Store ist unabhängig vom Graph-Store austauschbar.
- Bestehendes Verhalten (Cypher/Neo4j + In-Memory-Dev-Modus) bleibt unverändert.

### Nicht-Funktional

- **Non-Regression:** die bestehende Test-Suite bleibt grün; der Refactor ist verhaltensneutral.
- **Interface-First (ADR-0003):** die Naht ist ein Core-Vertrag, Implementierungen in den Provider-Teilen.
- **Endliche, gekapselte Operationen:** die Graph-Schicht nutzt eine geschlossene Menge semantischer
  Operationen — der `InMemoryCypherExecutor` beweist das bereits, indem er ~40 feste Query-Shapes
  re-implementiert.

## Betrachtete Optionen

### Option 0: `IGraphStore` als semantische Naht über den Operationen + `IVectorStore` entkoppeln

Ein `IGraphStore`-Vertrag kapselt die ~40 Operationen **semantisch** (UpsertRule, GetRulesByScope,
FindNeighbors, LoadContextRules, IngestEntities …) statt als Cypher-Strings. Die heutige Cypher-Logik wird
zu einem `CypherGraphStore` (nutzt weiter `ICypherExecutor`), der In-Memory-Dispatch zu einem
`InMemoryGraphStore`. Neue Backends (SQL/Kuzu) implementieren `IGraphStore` nativ. `IVectorStore` zieht
Embeddings/ANN aus dem Graph heraus (Neo4j-Vektorindex / pgvector / Qdrant / null).

**Positiv:**
- Nicht-Cypher-Backends andockbar; Vektor-Store unabhängig skalierbar.
- Verhaltensneutral machbar (Cypher-Logik wandert nur hinter den Vertrag).
- Der `InMemoryCypherExecutor` ist faktisch schon der Prototyp der semantischen Ebene — Machbarkeit belegt.
- Etabliert das Modul-Prinzip (interface + default + config) als Framework-Nordstern.

**Negativ:**
- Großer Refactor der zentralen Graph-Schicht (Risiko, Sorgfalt nötig).
- Zwei geschichtete Nähte (`IGraphStore` semantisch + `ICypherExecutor` für Cypher-Impls) — bewusst.

### Option 1: `ICypherExecutor` als einzige Naht behalten

Jedes Backend interpretiert Cypher.
**Positiv:** kein neuer Vertrag. **Negativ:** ein SQL-Backend bräuchte einen Cypher-Interpreter —
unrealistisch; löst das Kern-Ziel nicht.

### Option 2: Nur weitere Cypher-Provider (Memgraph/FalkorDB)

**Positiv:** minimal. **Negativ:** nicht-Cypher-Backends + entkoppelter Vektor-Store bleiben unmöglich —
Framework-Ziel verfehlt.

### Option 3: Generische Repository-/ORM-Schicht für alles

**Positiv:** maximale Abstraktion. **Negativ:** Over-Engineering, verwirft die spezialisierte Retrieval-/
Graph-Semantik, bricht Vorhandenes.

## Vorschlag des Autors

Option 0: `IGraphStore` als semantische Persistenz-Naht plus `IVectorStore`-Entkopplung. Erfüllt das
Framework-Ziel (steckbare Backends + Vektor-Store) verhaltensneutral, baut auf der belegt-endlichen
Operationsmenge auf und macht die Persistenz zum ersten echten „Modul" nach dem Framework-Prinzip.

## Entscheidung

**Gewählte Option:** „`IGraphStore` als semantische Naht + `IVectorStore` entkoppeln" (Option 0).

Die Wissensgraph-Schicht hängt künftig an `IGraphStore` (Core-Vertrag), statt Cypher direkt zu bauen;
`CypherGraphStore` (Neo4j/Memgraph über `ICypherExecutor`) und `InMemoryGraphStore` sind die
Default-Implementierungen, ein embedded/SQL-Backend folgt als weitere Implementierung. `IVectorStore`
kapselt Embedding-Persistenz + ANN-Suche. Der Umbau ist verhaltensneutral (Non-Regression-Gate:
Bestandstests grün) und erfolgt in kleinen Scheiben.

## Konsequenzen

### Positiv

- Persistenz + Vektor werden echte, config-gewählte Module → Fundament der SAP-artigen Framework-Vision.
- Neue Backends (SQLite/Kuzu/Postgres) + Vektor-Stores (pgvector/Qdrant) ohne Änderung der Graph-API.
- Retrieval unabhängig skalierbar (Vektor-Store getrennt).

### Negativ

- Zentraler, risikoreicher Refactor — nur in kleinen, getesteten Scheiben mit Non-Regression-Gate.
- Zusätzliche Schicht (semantischer Vertrag über dem Executor) — bewusst in Kauf genommen.

### Folge-Entscheidungen

- Konkrete `IGraphStore`-Methodensignaturen + Scheiben-Schnitt (verhaltensneutraler Adapter zuerst) — Plan.
- `IVectorStore`-Provider-Set (Neo4j-Index / pgvector / Qdrant / null) + Migration der heute im Graph
  liegenden Embeddings.
- Erstes nicht-Cypher-Backend (SQLite vs. Kuzu) — separate Folge-Entscheidung/ADR.

### Review

**Reality-Check geplant für:** 2026-09-01 (nach der ersten Backend-Scheibe).

## Weitere Informationen

### Scope

Betrifft `src/Core/Abstractions` (neue Verträge), `src/AKG/Graph` (CypherGraphStore/InMemoryGraphStore-
Umbau) und die DI-Verdrahtung. Die Wissensgraph-API (`IKnowledgeGraph`), TDK, Memory, Governance und MCP
bleiben funktional unverändert. Dieses ADR etabliert zugleich das übergreifende **Modul-Prinzip** (jede
Fähigkeit = Interface + Default-Impl + Config); die Persistenz ist das erste vollständig durchgezogene
Modul.

### Referenzen

- [ADR-0003](./0003-interface-first-fuer-injizierte-services.md) — Interface-First für injizierte Services
- [ADR-0009](./0009-hierarchisches-coarse-to-fine-retrieval.md) — Retrieval (Konsument des Vektor-Stores)
- `ROADMAP.md` — Track 4 (Backend-Flexibilität)
