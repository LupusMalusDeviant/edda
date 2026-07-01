# ADR-0010: LLM-Provider und Aktivierung der Auto-Extraktion (M2)

- **Status:** Vorgeschlagen
- **Datum:** 2026-07-01
- **Autor:** LupusMalusDeviant
- **Konsultiert:** —

## Kontext und Problemstellung

ADR-0001 hat den **optionalen LLM-Ingestion-Enricher hinter einem Flag** beschlossen und als offene
Folge-Entscheidung notiert: *„LLM-Provider für den Enricher: Wiederverwendung der bestehenden
Embeddings-/HTTP-Provider-Infrastruktur vs. eigener Client."* Für die Roadmap-Stufe **M2** (siehe
PRD-0001, Auto-Wissensgraph-Ingestion) wird der Enricher jetzt tatsächlich gebraucht: Er soll aus
Rohdaten Zusammenfassungen und Relationen zwischen existierenden Knoten ableiten.

Der „light"-Build ist bewusst ohne Chat-LLM gebaut (`CLAUDE.md`). M2 weicht das minimal auf — aber die
Frage ist, **wie**: Welcher Provider ist der Default, und wird der Enricher im light-Build überhaupt
verdrahtet, ohne das Alleinstellungsmerkmal „läuft lokal, ohne großes LLM" preiszugeben? Edda positioniert
sich (PRD, README-Narrativ) explizit als local-first, safety-first, „kein großes LLM nötig" — der Default
darf das nicht brechen.

**Verfeinert:** [ADR-0001](./0001-optionaler-llm-enricher-ingestion.md).

**Kernfrage:** Welcher LLM-Provider ist der Default für den Ingestion-Enricher, und wird der Enricher im
light-Build aktiviert — ohne den Zero-Infra-/Local-only-Charakter als Default aufzugeben?

## Anforderungen

### Funktional

- Der Enricher muss aus Rohtext eine Verdichtung + Relationsvorschläge (nur zu existierenden IDs) erzeugen (PRD-0001 FR-01/02).
- Der Provider muss austauschbar sein (PRD-0001 FR-04); mindestens ein lokaler und ein Cloud-Provider.
- Die Extraktion muss abschaltbar sein; der Default-Pfad bleibt ohne LLM (PRD-0001 FR-03).

### Nicht-Funktional

- **Local-only als Default:** ohne explizite Aktivierung verlässt kein Inhalt die Maschine.
- **Zero-Infra-Ethos:** die *empfohlene* Aktivierung soll ohne Cloud-Account/Key möglich sein.
- **Kein globaler Chat-Client** als Seiteneffekt (Interface-First, Regel 1) — nur ein Ingest-Zeit-Client.
- **Testbar ohne Infrastruktur** (Mock-Enricher), 100 % Coverage für neue Klassen (Regel 7).

## Betrachtete Optionen

### Option 0: Ollama-lokal als Default, Provider pluggable, Enricher opt-in im light-Build

Der bestehende `IIngestionEnricher`/`ILlmChatClient` wird im light-Build verdrahtet, aber per Default
**aus** (`INGESTION_ENRICHER` leer). Bei Aktivierung ist der empfohlene Default-Provider ein lokaler
Ollama; Cloud-Provider (Anthropic/OpenAI/Bedrock/OpenRouter/Gemini/custom) bleiben wählbar. Keys im
Credential-Store.

**Positiv:**
- Wahrt Local-only *und* Zero-Infra: die empfohlene Aktivierung läuft ohne Cloud/Key.
- Passt exakt zur dokumentierten Positionierung („kein großes LLM nötig", local-first).
- Default-Verhalten unverändert (aus) → keine Regression, kein Datenabfluss ohne Zutun.
- Provider pluggable → wer Qualität will, schaltet bewusst Cloud frei.

**Negativ:**
- Kleine lokale Modelle liefern rauschendere Graphen (analog Cognees ~32B-Caveat) — Qualität je nach Hardware.
- Ollama muss lokal laufen (eigener Prozess/Dienst) — „zero-infra" gilt fürs Cloud-Fehlen, nicht für Ollama selbst.

### Option 1: Cloud-Provider (Anthropic/OpenAI) als Default

Bei Aktivierung ist ein Cloud-LLM der empfohlene Default.

**Positiv:**
- Höhere, gleichmäßigere Extraktionsqualität unabhängig von lokaler Hardware.
- Kein lokaler Modell-Betrieb nötig.

**Negativ:**
- Bricht Zero-Infra/Local-only als empfohlenen Weg; braucht Account + Key.
- Widerspricht der „kein großes LLM / local-first"-Positionierung, die ein Differenzierungsmerkmal ist.
- Laufende API-Kosten + Datenabfluss als Normalfall der Aktivierung.

### Option 2: Enricher nicht im light-Build aktivieren (rein kuratiert bleiben)

Der Enricher bleibt wie heute dormant; der light-Build extrahiert nichts automatisch.

**Positiv:**
- Nullaufwand, keinerlei Aufweichung des No-LLM-Prinzips.
- Bleibt strikt deterministisch/reproduzierbar.

**Negativ:**
- Die zentrale M2-Lücke zu Cognee (Auto-KG) bleibt offen — Ziel verfehlt.
- Edda bleibt „Regelablage" statt mitwachsendem Gedächtnis.

### Option 3: Volle Chat-LLM-Runtime in den light-Build holen

Nicht nur ein Ingest-Client, sondern die komplette Chat-/Agent-Runtime des Monorepos übernehmen.

**Positiv:**
- Ermöglicht neben Extraktion auch `compile_knowledge`/`analyze_codebase` u. Ä.

**Negativ:**
- Massiver Scope-Bruch; widerspricht dem gesamten Zweck des light-Builds.
- Zieht Multi-Agent/Scheduling/Tools nach sich — genau das, was bewusst ausgeklammert wurde.

## Vorschlag des Autors

Option 0 trifft die Anforderungen am vollständigsten: Sie schließt die M2-Lücke (Auto-KG), lässt aber den
Default unverändert local-only und macht die *empfohlene* Aktivierung ohne Cloud/Key möglich — womit
Eddas Alleinstellungsmerkmal („läuft lokal, ohne großes LLM") als Default erhalten bleibt. Die schwächere
Qualität kleiner lokaler Modelle ist ein bewusst akzeptierter, dokumentierter Kompromiss; wer mehr braucht,
schaltet über denselben pluggable Mechanismus einen Cloud-Provider frei. Optionen 1/3 opfern das
Differenzierungsmerkmal, Option 2 verfehlt das M2-Ziel.

## Entscheidung

**Gewählte Option:** „Ollama-lokal als Default, Provider pluggable, Enricher opt-in im light-Build"

Ausschlaggebend war, dass der Default-Pfad unverändert local-only bleibt und die empfohlene Aktivierung
ohne externen Anbieter auskommt — das M2-Ziel wird erreicht, ohne die local-first-Positionierung
aufzugeben. Bewusst in Kauf genommen: variable Extraktionsqualität bei kleinen lokalen Modellen und der
Betrieb eines lokalen Ollama-Dienstes.

## Konsequenzen

### Positiv

- M2 (Auto-KG) wird möglich, ohne Zero-Infra/Local-only als Default zu verletzen.
- Wiederverwendung der bestehenden `ILlmChatClient`-Provider — kein neuer Client-Stack nötig.
- Klarer, abschaltbarer Opt-in — keine Regression, kein ungewollter Datenabfluss.

### Negativ

- Extraktionsqualität hängt vom lokalen Modell ab; auf schwacher Hardware rauschender Graph.
- Zwei Pfade (mit/ohne Enricher) müssen beide getestet werden (schon aus ADR-0001 bekannt).
- Ein lokaler Ollama-Dienst ist für die empfohlene Aktivierung Voraussetzung.

### Folge-Entscheidungen

- Segmentierung für die Extraktion: adaptives Chunking (ADR-0008) wiederverwenden vs. eigene „Atomisierung" — im M2-Implementation-Plan.
- Konkretes lokales Default-Modell für Ollama (Extraktionsqualität vs. Ressourcen) — Betrieb/Plan.
- PDF-Textextraktion (`IPdfTextExtractor`): offline-fähige Bibliothek wählen — Plan.
- Datenschutz-Hinweis in `docs/` für den Fall einer Cloud-Aktivierung (bereits aus ADR-0001 offen).

### Review

**Reality-Check geplant für:** 2026-08-30 (ca. 8 Wochen nach Entscheidung)

## Weitere Informationen

### Scope

Gilt für `src/AKG.Ingestion` (Enricher) und dessen DI-Verdrahtung im light-Build (`Edda.Hosting`). Betrifft
nicht das 4-Phasen-Retrieval, TDK oder die MCP-Exposition. Der Default-Build ohne `INGESTION_ENRICHER`
bleibt frei von LLM-Abhängigkeiten.

### Referenzen

- [ADR-0001](./0001-optionaler-llm-enricher-ingestion.md) — Optionaler LLM-Enricher (verfeinert durch dieses ADR)
- [ADR-0008](./0008-adaptives-chunking-retrieval-unterebene.md) — Adaptives Chunking
- `docs/prd/0001-m2-auto-wissensgraph-ingestion.md` — PRD zu M2
- `CLAUDE.md` — „Bewusst NICHT enthalten"
