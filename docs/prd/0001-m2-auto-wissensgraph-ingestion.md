# PRD-0001: M2 — Auto-Wissensgraph-Ingestion

- **Status:** Entwurf
- **Datum:** 2026-07-01
- **Autor:** LupusMalusDeviant
- **Stakeholder:** Product Owner: Repo-Eigner (LupusMalusDeviant); Consulted: angebundene Coding-Agenten (MCP-Clients)
- **Ersetzt:** —

## Problem / Motivation

Edda speichert heute **kuratiertes** Wissen: Regeln werden von Hand als Markdown verfasst, geparst und
per Keyword- + Semantik-Retrieval abgerufen. Edda kann **nicht selbst** aus Rohdaten (Code-Repos,
Dokumente, Notizen) einen Wissensgraphen aufbauen — es gibt keine automatische Extraktion von Entitäten
und Relationen. Im „light"-Build ist das sogar bewusst ausgeklammert (CLAUDE.md: „kein
`analyze_codebase` / Auto-Entity-Ingestion — bräuchte einen Chat-`IModelClient`").

Im direkten Vergleich mit Cognee (GraphRAG, das aus unstrukturierten Daten per LLM einen Graphen baut)
ist genau das die **größte Lücke**: In der Kategorien-Bewertung verlor Edda die Daily-AI-Agent- und
Firma-Code-Felder u. a. deshalb. Solange jeder Wissensbaustein von Hand geschrieben werden muss, bleibt
Edda eine „Regelablage" statt eines mitwachsenden Gedächtnisses.

**Do-Nothing-Szenario:** Edda skaliert nicht über handgepflegtes Wissen hinaus. Angebundene Agenten
müssen weiter das Dateisystem scannen, statt aus einem gewachsenen, verknüpften Graphen zu schöpfen —
der Kern-Nutzen („persistentes Langzeitgedächtnis") bleibt unter seinem Potenzial.

## Ziele

- Aus **mindestens 3 Rohquellen-Typen** (Dateisystem, Git, Markdown/PDF) automatisch typisierte
  Knoten + Relationen erzeugen — ohne dass ein Mensch Regeln schreibt.
- Die LLM-Extraktion ist **per Flag abschaltbar**; ohne sie bleibt das heutige Verhalten (kuratiert,
  keyword-basiert, ohne LLM) **unverändert** — belegt durch unveränderte bestehende Test-Suite.
- **Lokaler Betrieb möglich:** mit einem lokalen LLM (Ollama) läuft die Extraktion ohne Cloud/Key.
- Extraktion **erfindet keine Knoten**: gegen ein Referenz-Datenset entstehen **0** Relationen zu
  nicht existierenden Ziel-IDs.
- Der Entity-Layer trägt in der Kontext-Kompilierung messbar bei (Retrieval-Benchmark F48 ≥ gleichwertig
  gegenüber „ohne Entity-Layer").

## Non-Goals

- **Kein** episodisches Gedächtnis (`remember`/`recall`/`forget`) — gehört zu M3.
- **Keine** Mandantenfähigkeit (Tenants/Rollen) — gehört zu M3.
- **Keine** eigene Chat-LLM-Runtime oder Multi-Agent-Schleife — M2 braucht nur einen reinen
  Extraktions-/Enrichment-Client beim Ingest, keinen Agenten.
- **Keine** 30+ Connectoren wie Cognee — nur die pragmatischen ersten (FS/Git/Markdown/PDF);
  Slack/Notion/Confluence sind ausdrücklich später.
- **Kein** Cloud-Zwang — eine reine Cloud-only-Lösung ist nicht akzeptabel (Zero-Infra-Ethos).

## Zielgruppen / Personas

### Privater Entwickler (lokaler Wissensspeicher)

- Kontext: nutzt Edda lokal, wirft eigene Repos/Notizen ein, arbeitet mit Coding-Agenten.
- Pain Point: manuelle Regelpflege skaliert nicht; Wissen aus vorhandenem Code wird nicht genutzt.

### Team-Wissensbasis-Betreiber

- Kontext: betreibt Edda für ein Team, will Doku und Repos einspeisen, damit Agenten konsistent darauf zugreifen.
- Pain Point: Wissen liegt verstreut (Repos, Wikis) statt als abfragbarer, verknüpfter Graph.

### Angebundener Coding-Agent (betroffen, read-only)

- Kontext: greift über MCP auf Edda zu (Claude Code, Cursor).
- Pain Point: dünner Graph → wenig Kontext; profitiert von automatisch extrahierten Entitäten/Relationen.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Beim Ingest kann optional ein LLM-Enricher aufgerufen werden, der aus dem Rohtext eine 1–3-Satz-Zusammenfassung und vorgeschlagene Relationen erzeugt. | Must |
| FR-02 | Der Enricher schlägt Relationen ausschließlich zu Knoten aus dem bekannten Kandidaten-Set vor; erfundene Ziele werden verworfen. | Must |
| FR-03 | Die LLM-Extraktion ist per Konfiguration abschaltbar; im Aus-Zustand ist der kuratierte/keyword-Pfad bit-identisch zum heutigen Verhalten. | Must |
| FR-04 | Der LLM-Provider ist austauschbar (mind. lokal Ollama + ein Cloud-Provider); der API-Key liegt nie im Code/Settings, sondern im Credential-Store. | Must |
| FR-05 | Mindestens drei Quell-Connectoren sind produktiv nutzbar: Dateisystem, Git, Markdown. | Must |
| FR-06 | Enricher-/Provider-Fehler brechen den Ingest nicht ab (best-effort); der Rohknoten wird trotzdem gespeichert. | Must |
| FR-07 | Ingestierte Fremdinhalte durchlaufen vor einem LLM-Call die Sanitization/Redaction (Prompt-Injection-Schutz). | Must |
| FR-08 | Re-Ingest inhaltlich unveränderter Quellen erzeugt keine Duplikate (Content-Hash-Idempotenz). | Should |
| FR-09 | Extrahierte Entitäten fließen in den Entity-Layer (`BuildEntityContextAsync`) und erscheinen im kompilierten Kontext als „verwandte Entitäten". | Should |
| FR-10 | Große Importe laufen im Bulk-Modus (eine Embedding-Rebuild-Passe am Ende) statt pro Knoten zu blockieren. | Should |
| FR-11 | PDF-Quellen können extrahiert und ingestiert werden. | Could |
| FR-12 | Ingest-Fortschritt ist über den ActivityTracker in der UI sichtbar. | Could |

## Nicht-Funktionale Anforderungen

- **Betrieb lokal-only:** Mit Ollama läuft M2 vollständig ohne Cloud-Aufruf oder API-Key.
- **Kosten:** Cloud-Extraktion ist kostenkontrollierbar — Eingabe-Cap pro Dokument (der bestehende
  `LlmIngestionEnricher` begrenzt bereits auf ~6000 Zeichen).
- **Robustheit:** Ohne verfügbaren LLM degradiert das System sauber (kein Hard-Fail; Rohknoten bleiben).
- **Nicht-Regression:** Mit deaktivierter Extraktion bleibt die bestehende Test-Suite unverändert grün.

## User Stories / Use Cases

- **US-01:** Als privater Entwickler möchte ich ein lokales Repo ingesten, damit Edda automatisch
  verknüpftes Code-Wissen bereitstellt, ohne dass ich Regeln von Hand schreibe.
- **US-02:** Als Team-Betreiber möchte ich die LLM-Extraktion auf einen lokalen Ollama zeigen, damit
  keine Daten die Maschine verlassen.
- **US-03:** Als Betreiber möchte ich die LLM-Extraktion abschalten können, damit Edda im Zweifel als
  schlanke Keyword-Basis unverändert weiterläuft.
- **US-04:** Als angebundener Agent möchte ich beim Kontext-Abruf verwandte Entitäten sehen, damit
  Antworten mehr Zusammenhang bekommen.

### Detaillierter Flow (Ingest mit Extraktion)

```
Given ein konfigurierter Quell-Connector und aktivierte LLM-Extraktion
When der Ingest eine Rohdatei einliest
Then wird der Inhalt sanitisiert, (optional) vom Enricher zusammengefasst + mit Relationen zu
     bekannten Knoten angereichert, als typisierter Knoten gespeichert und idempotent verlinkt
```

## Akzeptanzkriterien / Success Metrics

- Alle FR-Must sind implementiert und durch **Unit-Tests mit gemocktem Enricher** (ohne echten LLM) abgedeckt.
- Ein Ingest-Lauf über ein Beispiel-Repo erzeugt im Graph typisierte Entitäten + Relationen; ein
  zweiter Lauf über unveränderten Input erzeugt **keine** Duplikate.
- Mit deaktivierter Extraktion ist die bestehende Test-Suite **unverändert grün** (Nicht-Regression).
- Gegen ein Referenz-Datenset entstehen **0** Relationen zu nicht existierenden Knoten (FR-02).
- Der Retrieval-Benchmark (F48) mit aktivem Entity-Layer liefert **≥ gleichwertige** Recall@k / MRR
  gegenüber dem Lauf ohne Entity-Layer, gemessen über das Referenz-Datenset.

## Offene Fragen

- **LLM-Provider-Default:** lokal Ollama (Zero-Infra-Ethos) vs. Cloud (Anthropic/OpenAI, höhere
  Qualität)? → ADR „LLM-Provider für Ingestion-Enrichment" (Bezug: bestehendes ADR-0001).
- **Architektur-Grenze:** Bleibt der Enricher ein reiner Ingest-Client (kein Chat-Runtime), sodass die
  bewusste light-Build-Entscheidung nur minimal aufgeweicht wird? → ADR.
- **Segmentierung:** Reicht das bestehende adaptive Chunking (ADR-0008) für die Entitäts-Extraktion,
  oder braucht es eine eigene „Atomisierung"?
- **PDF-Extraktion:** Welche lokale Bibliothek für `IPdfTextExtractor` (offline-fähig)?
- **Extraktions-Qualität ohne echten LLM:** nur mit Mocks verifizierbar — wie wird die reale Qualität
  (später, mit Provider) abgenommen?

## Referenzen

- ADR-0001 — Optionaler LLM-Enricher (Ingestion)
- ADR-0008 — Adaptives Chunking / Retrieval-Unterebene
- ADR-0009 — Hierarchisches coarse-to-fine Retrieval
- `ROADMAP.md` — Track 1 (Auto-Wissensgraph), Track 4.2 (Vektor-Store entkoppeln)
- Cognee-Vergleich (Session-Analyse) — GraphRAG-Referenz

## Nächste Schritte

- ADR für die LLM-Provider-Wahl erstellen (via `adr-writer`, ggf. mit `tech-choice-helper` als Vorstufe).
- Danach Implementation-Plan ableiten (via `plan-builder`, PRD-basiert).
