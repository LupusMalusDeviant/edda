# ADR-0011: Design des episodischen Agent-Gedächtnisses (M3)

- **Status:** Akzeptiert
- **Datum:** 2026-07-01
- **Autor:** LupusMalusDeviant
- **Konsultiert:** —

## Kontext und Problemstellung

M3 (PRD-0002) führt ein episodisches Gedächtnis ein: `remember` / `recall` / `forget` plus
Konsolidierung von Sitzungswissen. Edda hat bereits einen user-skopierten Wissensgraphen mit Retrieval
(4-Phasen-Kompilierung) und seit 0.2 einen Confidence-Decay. Die Frage ist, ob das Gedächtnis diese
bestehende Maschinerie **wiederverwendet** oder als **eigenes Subsystem** entsteht.

**Kernfrage:** Wie wird episodisches Gedächtnis modelliert — als user-skopierte AKG-Knoten oder als
separater Store — und wie werden Sitzungen konsolidiert, ohne ein zweites Retrieval-/Speicher-System zu
etablieren?

## Anforderungen

### Funktional

- Fakten merken/abrufen/vergessen, user-skopiert (PRD-0002 FR-01..05).
- Abruf nutzt bestehende Relevanz-Logik statt eigener Retrieval-Implementierung.
- Vergessen bindet an den vorhandenen Decay (0.2) an.

### Nicht-Funktional

- Kein zweites Retrieval-System (Wartung/Drift vermeiden).
- Safety-first: Schreib-Tools über MCP default-deny.
- Testbar mit Mocks, 100 % Coverage neuer Klassen.

## Betrachtete Optionen

### Option 0: User-skopierte AKG-Gedächtnis-Knoten + dünne API

Gedächtnis = Knoten im bestehenden Graph mit `SourceType=memory`, user-skopiert. `remember`/`recall`/
`forget` sind eine dünne Schicht: `recall` ruft die bestehende Kontext-Kompilierung, auf Gedächtnis-Knoten
gefiltert; `forget` löscht Knoten; Vergessen/Verblassen über den Decay (0.2).

**Positiv:**
- Wiederverwendung von Graph, Retrieval, Decay und User-Scoping — minimale neue Fläche.
- Ein Speicher, ein Retrieval-Pfad → kein Drift zwischen zwei Systemen.
- Fügt sich in Safety-first-MCP (Schreib-Tools default-deny) und bestehende Tests ein.

**Negativ:**
- Gedächtnis und kuratierte Regeln teilen sich den Graph — Trennung nur über `SourceType`/Scope.
- „Episode/Sitzung" ist kein first-class Konzept, sondern über Metadaten abgebildet.

### Option 1: Separater Episoden-Store (SQLite/eigener Graph)

Eigenes Schema/Speicher nur für Episoden, mit eigenem Abruf.

**Positiv:**
- Klare Trennung episodisch vs. kuratiert; „Episode" als first-class Modell möglich.

**Negativ:**
- Zweites Retrieval-/Ranking-System → Duplizierung + Drift-Gefahr.
- Decay/Scoping/MCP-Exposition müssten dupliziert werden.

### Option 2: Volles Cognee-artiges Sitzungspuffer- + Konsolidierungs-Subsystem

Sitzungs-Kurzzeitpuffer + eigene `improve`/`memify`-Konsolidierung wie bei Cognee.

**Positiv:**
- Reichste Semantik (Selbstverbesserung, gewichtete Konsolidierung).

**Negativ:**
- Großer neuer Subsystem-Aufwand; overkill für die Zielgröße; braucht faktisch einen LLM.

## Vorschlag des Autors

Option 0 erfüllt die Anforderungen am schlanksten: Gedächtnis erbt Graph, Retrieval, Decay und Scoping;
die neue Fläche ist eine dünne `remember`/`recall`/`forget`-API plus eine Konsolidierungs-Routine. Die
schwächere Trennung (Metadaten statt eigenem Store) ist ein bewusst kleiner Preis gegenüber einem zweiten
Speicher-/Retrieval-System (Option 1) oder einem ganzen Subsystem (Option 2).

## Entscheidung

**Gewählte Option:** „User-skopierte AKG-Gedächtnis-Knoten + dünne API"

Ausschlaggebend: ein Speicher/ein Retrieval-Pfad, Wiederverwendung von Decay + Scoping + Safety-MCP.
Bewusst akzeptiert: episodisch und kuratiert teilen sich den Graph (Trennung über `SourceType`/Scope).

## Konsequenzen

### Positiv

- Kleinste sinnvolle Erweiterung; nutzt bestehende Stärken (Decay, Scoping, MCP-Sicherheit).
- Konsistentes Retrieval für Regeln und Erinnerungen.

### Negativ

- „Episode/Sitzung" ist kein first-class Modell, sondern Metadaten-basiert.
- Graph enthält zweierlei Inhalt (kuratiert + gemerkt) — Filter/Scoping müssen sauber greifen.

### Folge-Entscheidungen

- Konsolidierungs-Trigger + Dedup-Strategie (Sitzungsende) — im M3-Plan.
- Ob `recall` per Default in der MCP-Allowlist steht (Vorschlag: opt-in) — im M3-Plan/Betrieb.

### Review

**Reality-Check geplant für:** 2026-08-30

## Weitere Informationen

### Scope

Betrifft `src/AKG` (Gedächtnis-Knoten/Retrieval-Scope), `src/Agent` (neue Tools) und die MCP-Exposition.
Der kuratierte Regel-Pfad bleibt funktional unverändert.

### Referenzen

- `docs/prd/0002-m3-episodisches-agent-gedaechtnis.md`
- ROADMAP.md — Track 2; Confidence-Decay (0.2, umgesetzt)
