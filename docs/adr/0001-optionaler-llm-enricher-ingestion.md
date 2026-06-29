# ADR-0001: Optionaler LLM-Enricher für die AKG-Ingestion

- **Status:** Akzeptiert
- **Datum:** 2026-06-15
- **Autor:** Eric Lenk

## Kontext und Problemstellung

Edda ist bewusst ohne Chat-LLM-Runtime gebaut. `CLAUDE.md` hält fest: *„Bewusst NICHT enthalten: Chat-LLM-Runtime … Daher gibt es kein `compile_knowledge`, `analyze_codebase` oder Auto-Entity-Ingestion (diese bräuchten einen Chat-`IModelClient`)."* Der einzige externe Modell-Zugriff sind heute die Embedding-Provider; eine Null-Variante erlaubt vollständig lokalen Betrieb.

Jetzt entsteht eine Ingestion-Pipeline, die externe Quellen (zuerst Git-Repos, später Jira/Awork) in AKG-Wissensknoten als `.md`-Dateien mit Frontmatter überführt. Der eigentliche Mehrwert von AKG ist nicht der reine Text, sondern der **Graph mit Relationen** (`requires`, `supersedes`, `conflictsWith`, `implies`, `exceptionFor`). Aus Git-Doku lassen sich manche Relationen deterministisch ableiten — ADR-Supersede-Vermerke, Verzeichnisstruktur, Markdown-Querverweise. Semantische Relationen und das Verdichten langer Doku zu prägnanten Wissensnotizen erfordern dagegen Sprachverständnis und sind ohne LLM nur grob heuristisch lösbar.

Damit entsteht eine Spannung zwischen der gewünschten Relations- und Inhaltsqualität und dem dokumentierten Local-only/No-LLM-Prinzip, das ein Alleinstellungsmerkmal gegenüber Memory-Layern wie cognee oder mem0 ist (diese verlangen einen LLM zwingend für ihre Ingestion).

**Kernfrage:** Soll die Ingestion einen LLM nutzen, um Inhalte zu verdichten und semantische Relationen abzuleiten — und falls ja, wie, ohne das Local-only/No-LLM-Prinzip von Edda aufzugeben?

## Anforderungen

### Funktional

- Das deterministische Relations-Mapping aus nativen Quell-Strukturen (ADR-Supersedes, Verzeichnisse, Markdown-Links) muss als Basis immer und ohne externe Abhängigkeit funktionieren.
- Optional ableitbar: semantische Relationen zwischen bereits importierten Knoten sowie eine prägnante Verdichtung langer Quell-Inhalte.
- Vorgeschlagene Relationen dürfen ausschließlich **existierende** importierte Knoten-IDs referenzieren — kein Erfinden neuer Knoten oder IDs.

### Nicht-Funktional

- **Local-only als Default:** Ohne explizite Aktivierung darf kein Quellinhalt das System verlassen.
- **Determinismus im Default-Pfad:** Ein Import ohne Enricher muss reproduzierbar dasselbe Ergebnis liefern.
- **Datenschutz/Compliance:** Bei Aktivierung gehen Quellinhalte an einen externen LLM-Anbieter; das muss bewusst opt-in und dokumentiert sein.
- **Testbarkeit ohne Infrastruktur** (Mocks), 100 % Unit-Coverage für neue Klassen (Repo-Regel 7).
- **Interface-First** (Repo-Regel 1): kein globaler Chat-Client als Seiteneffekt, sondern eine eng umrissene Abstraktion.

## Betrachtete Optionen

### Option 0: Kein LLM (rein deterministisch)

Relationen und Inhalte entstehen ausschließlich aus maschinell erkennbaren Quell-Strukturen.

**Positiv:**
- Wahrt das No-LLM/Local-only-Prinzip strikt und ohne Ausnahme.
- Vollständig deterministisch und reproduzierbar; keine API-Kosten, keine Latenz.
- Keine neue externe Abhängigkeit, kein Datenschutz-Risiko durch ausgehende Inhalte.

**Negativ:**
- Relations-Qualität bei freier Git-Doku bleibt dürftig — nur, was sich strukturell ablesen lässt.
- Keine Verdichtung: lange README-/Doku-Texte landen ungekürzt als Wissensknoten und blähen das Kontext-Budget auf.
- Der zentrale AKG-Mehrwert (reicher Relationsgraph) wird für die wichtigste erste Quelle (Git) nur teilweise erreicht.

### Option 1: Optionaler LLM-Enricher hinter Flag

Neues Interface `IIngestionEnricher` in `Core/` mit Default-Implementierung `NullIngestionEnricher` (No-op). Eine LLM-gestützte Implementierung wird nur bei `INGESTION_ENRICHER=llm` plus API-Key aktiv und ist auf zwei eng umrissene Aufgaben beschränkt: Inhalte verdichten und Relationen **zwischen bereits importierten IDs** vorschlagen.

**Positiv:**
- Default-Build bleibt deterministisch und local-only; das dokumentierte Prinzip gilt unverändert als Standard.
- LLM-Nutzung ist gekapselt, abschaltbar und klar abgegrenzt — kein globaler Chat-`IModelClient`, der das System durchzieht.
- Höhere Relations-/Inhaltsqualität genau dort, wo der Betreiber sie bewusst bezahlt und freischaltet.
- Begrenzung auf existierende IDs verhindert halluzinierte Knoten und hält den Graph konsistent.

**Negativ:**
- Zwei Code-Pfade (mit/ohne Enricher) erhöhen Test- und Wartungsaufwand; beide brauchen volle Coverage.
- Das Ergebnis ist im aktivierten Modus nicht mehr deterministisch.
- Das dokumentierte „keine Chat-LLM"-Prinzip wird aufgeweicht — wenn auch nur opt-in; `CLAUDE.md` muss präzisiert werden.
- Datenschutz-Verantwortung beim Betreiber: aktivierter Enricher sendet Quellinhalte an Dritte.

### Option 2: Immer-LLM (cognee-/mem0-Stil)

Quellinhalte werden grundsätzlich per LLM zu Knoten und Relationen verarbeitet, wie bei den vergleichbaren Memory-Plattformen.

**Positiv:**
- Maximale Abdeckung und Relations-Reichtum, gleiches Verfahren über alle Quellen.
- Konzeptionell einfacher: nur ein Pfad, keine Fallunterscheidung.

**Negativ:**
- Stärkster Bruch mit dem dokumentierten Prinzip; das Alleinstellungsmerkmal „läuft ohne LLM" ginge verloren.
- Verpflichtende API-Kosten, Latenz und Nichtdeterminismus für jeden Import.
- Local-only-Betrieb unmöglich; Datenschutz-Hürde bei jeder Nutzung.

## Vorschlag des Autors

Option 1 trifft die Anforderungen am vollständigsten: Sie liefert die für Git-Quellen nötige semantische Tiefe, ohne den Local-only-Default und damit das Kern-Differenzierungsmerkmal aufzugeben. Der Mehraufwand zweier Code-Pfade ist überschaubar, weil der Enricher hinter einer schmalen Schnittstelle sitzt und die `Null`-Implementierung der Embeddings-Architektur bereits als Muster vorliegt. Die Begrenzung auf Verdichtung und Relations-Vorschläge zwischen existierenden IDs hält das Risiko (halluzinierte Knoten, Datenabfluss) klein und kontrollierbar.

## Entscheidung

**Gewählte Option:** „Optionaler LLM-Enricher hinter Flag"

Ausschlaggebend waren der Local-only-Default und die Kapselung: Das System bleibt ohne Konfiguration rein deterministisch und ohne externen Modellzugriff, während Betreiber die höhere Qualität bewusst freischalten können. Der akzeptierte Preis sind zwei zu pflegende und zu testende Code-Pfade sowie eine Präzisierung des `CLAUDE.md`-Prinzips.

## Konsequenzen

### Positiv

- Der Standard-Build verletzt das No-LLM/Local-only-Prinzip nicht; Edda behält sein Differenzierungsmerkmal.
- Git-Ingestion kann optional einen reichen Relationsgraphen und verdichtete Wissensknoten erzeugen.
- Die schmale `IIngestionEnricher`-Abstraktion ist quellenunabhängig und später auch für Jira/Awork nutzbar.

### Negativ

- Höhere Test-Matrix: deterministischer Pfad und Enricher-Pfad müssen beide mit voller Coverage abgedeckt werden.
- Im aktivierten Modus sind Imports nicht mehr reproduzierbar.
- Betreiber tragen bei Aktivierung die Datenschutz-Verantwortung für ausgehende Quellinhalte.

### Folge-Entscheidungen

- LLM-Provider für den Enricher: Wiederverwendung der bestehenden Embeddings-/HTTP-Provider-Infrastruktur vs. eigener Client.
- Git-Client-Technologie für den Remote-Klon (LibGit2Sharp vs. `git`-CLI über eine Prozess-Abstraktion) — eigener ADR-Kandidat.
- Präzisierung des `CLAUDE.md`-Prinzip-Texts: LLM-Nutzung existiert ausschließlich opt-in in der Ingestion.
- Datenschutz-Hinweis in `docs/` (welche Inhalte bei aktiviertem Flag an wen gehen).

### Review

**Reality-Check geplant für:** 2026-08-10 (ca. 8 Wochen nach Entscheidung)

## Weitere Informationen

### Scope

Gilt für das neue Projekt `src/AKG.Ingestion` und die Ingestion-Pipeline. Die bestehenden MCP-Tools, das 4-Phasen-Retrieval und die TDK-Validierung sind nicht betroffen. Der Default-Build (ohne `INGESTION_ENRICHER`) bleibt frei von LLM-Abhängigkeiten.

### Referenzen

- `CLAUDE.md` — Abschnitt „Was das ist" / „Bewusst NICHT enthalten".
- Folgt: Implementierungs-Plan unter `docs/plans/` (Ingestion-Pipeline, Git zuerst).
