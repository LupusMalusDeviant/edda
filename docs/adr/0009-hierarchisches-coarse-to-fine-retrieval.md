# ADR-0009: Hierarchisches Coarse-to-Fine-Retrieval mit Head-Vektoren

- **Status:** Akzeptiert
- **Datum:** 2026-06-19
- **Autor:** Eric Lenk
- **Konsultiert:** —

## Kontext und Problemstellung

Der Wissensgraph wächst stark: aktuell rund 19.000 `:Rule`-Knoten — überwiegend ingestete Repository-Dateien, je Datei mehrere eingebettete `:RuleChunk`. Mit dieser Masse skaliert das heutige Retrieval schlecht und liefert zu unscharfe Treffer (Inhalte aus thematisch fremden Repos rutschen über die Ähnlichkeitsschwelle).

Die Kontext-Kompilierung arbeitet heute „flach": Sie lädt zunächst ALLE in-scope `:Rule` (ein O(n)-Full-Scan über den gesamten Graphen), scort per Keyword, fährt dann eine GLOBALE Chunk-Vektorsuche über den `chunk_embeddings`-Index (alle `:RuleChunk`), und ergänzt um Graph-Expansion und World-Knowledge. Sowohl der Full-Scan als auch die Vektor-Suchmenge wachsen mit jedem ingesteten Repo linear mit.

Die Hierarchie liegt strukturell bereits vor, allerdings nur als ID-Präfix-Nesting (keine expliziten Kanten): Repo-Headnode `git:<repo>` → Datei-Leaves `git:<repo>:<pfad>` → `:RuleChunk`. Einbettungen tragen ausschließlich `:RuleChunk`; Headnodes haben nur einen generischen Pseudo-Body („Git repository X") und sind damit faktisch nicht semantisch auffindbar. Ein Subtree lässt sich über `parentId STARTS WITH 'git:<repo>:'` eingrenzen.

**Kernfrage:** Wie wird das Retrieval mit der Knotenmasse skalierbar und präziser, ohne bei jeder Anfrage den gesamten Graphen zu scannen?

## Anforderungen

### Funktional

- Vorab-Eingrenzung („Pre-Pruning"): erst die thematisch passende(n) Repo-Head(s) per semantischer Suche bestimmen, dann nur deren Subtree fein durchsuchen.
- Feine Auswahl innerhalb der gewählten Heads über das Anfrage-Embedding gegen die vorhandenen Datei-Chunks.
- Recall-Sicherung: wird kein Head sicher getroffen, darf kein relevanter Inhalt verloren gehen.

### Nicht-Funktional

- Skaliert mit weiter wachsender Knotenzahl — kein je Anfrage linear wachsender Full-Scan.
- Keine Abhängigkeit von einer Chat-LLM-Runtime (Edda hat bewusst keine; ein Ingestion-LLM ist nur optional).
- Baut auf der bestehenden Embedding-/Chunk-Infrastruktur und dem ID-Präfix-Schema auf.
- Head-Repräsentationen halten sich automatisch aktuell (im Embedding-Backfill).

## Betrachtete Optionen

Die Stufe-2-Feinsuche (Chunk-Vektorsuche, eingegrenzt per ID-Präfix) ist in allen Optionen identisch. Die Entscheidung dreht sich um die **Quelle des Head-Vektors**, der Stufe 1 (das Pre-Pruning) überhaupt erst möglich macht.

### Option 0: Centroid der Datei-Chunk-Embeddings

Jeder Repo-Head erhält einen abgeleiteten Vektor = Mittelwert (optional mehrere Centroids via Clustering) der Chunk-Embeddings seiner Dateien.

**Positiv:**
- Kein LLM nötig; nutzt ausschließlich bereits vorhandene Chunk-Vektoren.
- Aktualisiert sich beim ohnehin laufenden Embedding-Backfill mit.
- Günstig in Rechen- und Speicheraufwand.

**Negativ:**
- Ein einzelner Mittelwert verschwimmt bei thematisch breiten Repos (Mitigation: mehrere Centroids je Head).
- Centroids müssen bei neuen/geänderten Dateien neu berechnet werden.

### Option 1: LLM-Summary je Repo

Pro Repo eine generierte Zusammenfassung, die anschließend embedded wird.

**Positiv:**
- Schärferes, themen-fokussiertes Signal; robust auch bei breiten Repos.

**Negativ:**
- Braucht den optionalen `INGESTION_LLM`-Provider — widerspricht der bewussten „kein-LLM-Pflicht"-Linie von Edda.
- LLM-Call samt Kosten/Latenz je Repo beim Ingest; zusätzliche Fehlerquelle.

### Option 2: Nur Metadaten embedden

Repo-Name + Dateiliste + README als Head-Text embedden.

**Positiv:**
- Billig und sofort verfügbar, keine zusätzliche Abhängigkeit.

**Negativ:**
- Schwaches Signal — kennt den tatsächlichen Inhalt nicht; viele Repos haben kein aussagekräftiges README.

## Vorschlag des Autors

Option 0. Sie liefert das Pre-Pruning ohne neue Laufzeit-Abhängigkeit, indem sie die bereits berechneten Chunk-Vektoren wiederverwendet, und fügt sich nahtlos in den frisch eingeführten resilienten Embedding-Backfill ein (die Centroid-Berechnung hängt sich an denselben Lauf). Die zentrale Schwäche — Verschwommenheit breiter Repos — wird durch mehrere Centroids je Head entschärft; das Recall-Risiko durch eine Top-k-Auswahl plus Fallback auf die globale Suche, wenn kein Head die Schwelle erreicht. Die LLM-Summary (Option 1) wäre semantisch schärfer, kollidiert aber mit dem Architekturprinzip „keine Chat-LLM-Runtime"; reine Metadaten (Option 2) sind zu schwach für verlässliches Routing.

## Entscheidung

**Gewählte Option:** "Centroid der Datei-Chunk-Embeddings"

Das Retrieval wird zweistufig: **Stufe 1** sucht das Anfrage-Embedding gegen einen separaten Head-Vektor-Index (Centroids je Repo) und wählt die Top-k Heads; **Stufe 2** führt die bestehende Chunk-Vektorsuche aus, eingegrenzt auf die Subtrees dieser Heads (`parentId STARTS WITH …`). Damit entfällt der heutige globale Full-Scan der Lade-/Scoring-Phase. Den Ausschlag gaben die Nicht-funktionalen Anforderungen (Skalierung ohne wachsenden Full-Scan, keine LLM-Abhängigkeit); die bewusst akzeptierten Nachteile sind der Pflegeaufwand der Centroids und ein durch Top-k + Fallback abgesichertes Recall-Restrisiko.

## Konsequenzen

### Positiv

- Anfragekosten entkoppeln sich von der Gesamt-Knotenzahl: gescort wird nur noch innerhalb der vorab gewählten Subtrees.
- Weniger Rauschen — fremde Repos werden vor der Feinsuche ausgeschlossen.
- Keine neue Laufzeit-Abhängigkeit; Head-Vektoren entstehen aus vorhandenen Chunk-Embeddings.

### Negativ

- Head-Centroids sind ein zusätzlicher, zu pflegender Datenstand (Neuberechnung bei geänderten Dateien, am besten im Backfill).
- Centroid-Unschärfe bei thematisch breiten Repos bleibt ein Qualitätsrisiko, das Multi-Centroid nur abmildert, nicht eliminiert.
- Wählt Stufe 1 den falschen Head, sinkt der Recall — abgefangen über Top-k und Schwellen-Fallback, aber nicht ausgeschlossen.

### Folge-Entscheidungen

- Speicher-/Indexform der Head-Vektoren (eigener Index vs. markierte Chunks mit `level`-Flag) und ob/ab wann Multi-Centroid (Cluster-Anzahl je Head) aktiviert wird.
- Konkrete Parameter: Anzahl Top-k Heads, Head-Ähnlichkeitsschwelle, Fallback-Bedingung — gehören in den Implementation-Plan.
- Verhalten für Nicht-Git-/Standalone-Regeln (eigener Head vs. direkte Teilnahme an Stufe 2).

### Review

**Reality-Check geplant für:** 2026-07-31 (ca. 6 Wochen nach Entscheidung; an echten Anfragen messen, ob Recall/Präzision und das Phasen-Timing den erhofften Gewinn zeigen).

## Weitere Informationen

### Scope

Betrifft die AKG-Retrieval-Pipeline (`ContextCompiler`, `SemanticBooster`) und die Embedding-Schicht (`Neo4jEmbeddingCache`, `EmbeddingBackfillHostedService`). Primär für die ID-präfix-basierte Repo-/Upload-Hierarchie (`git:<repo>:…`, `upload:<source>:…`). Standalone-Regeln ohne Hierarchie sind ein gesonderter Fall (siehe Folge-Entscheidungen). Die konkrete Umsetzung wird in einem separaten Implementation-Plan ausgearbeitet.

### Referenzen

- [0008-adaptives-chunking-retrieval-unterebene.md](./0008-adaptives-chunking-retrieval-unterebene.md) — führt die `:RuleChunk`-Unterebene + adaptives Chunking ein; dieses ADR erweitert deren Retrieval um die hierarchische Head-Stufe (Ergänzung, kein Ersatz).
- Branch `feat/memory-repositioning` — Memory-Repositioning + resilienter Embedding-Backfill, an den die Centroid-Berechnung andockt.
