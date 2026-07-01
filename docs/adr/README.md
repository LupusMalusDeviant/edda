# Architecture Decision Records

Chronologisches Verzeichnis aller Architekturentscheidungen dieses Repos. Jede Zeile führt zu einem einzelnen ADR.

## Was ist ein ADR?

Ein Architecture Decision Record dokumentiert eine einzelne, wichtige Architekturentscheidung — inklusive des damaligen Kontexts, der betrachteten Optionen und der bewusst in Kauf genommenen Konsequenzen. ADRs sind unveränderliche Zeitkapseln: Sie werden nicht überschrieben, sondern bei Revisionen durch neue ADRs ersetzt (Supersede).

## Status-Legende

- **Vorgeschlagen** — in Review, noch nicht beschlossen.
- **Akzeptiert** — beschlossen und gültig.
- **Abgelehnt** — Vorschlag wurde verworfen (bleibt als Lernerfahrung im Log).
- **Veraltet** — durch ein neueres ADR ersetzt.

## Decision Log

| Nr. | Titel | Status | Datum | Ersetzt durch |
|-----|-------|--------|-------|----------------|
| [0001](./0001-optionaler-llm-enricher-ingestion.md) | Optionaler LLM-Enricher für die AKG-Ingestion | Akzeptiert | 2026-06-15 | — |
| [0002](./0002-git-client-technologie-ingestion.md) | Git-Client-Technologie für die Ingestion (LibGit2Sharp) | Akzeptiert | 2026-06-15 | — |
| [0003](./0003-interface-first-fuer-injizierte-services.md) | Interface-First für alle injizierten Services | Akzeptiert | 2026-06-17 | — |
| [0004](./0004-live-apply-laufzeit-konfiguration.md) | Live-Apply der Laufzeit-Konfiguration über Resolving-Fassaden | Vorgeschlagen | 2026-06-17 | — |
| [0005](./0005-config-getriebene-wissensquellen-connectoren.md) | Config-getriebenes Connector-Modell für Wissensquellen | Vorgeschlagen | 2026-06-17 | — |
| [0006](./0006-generische-quell-connectoren-http-mcp.md) | Generische, config-getriebene Quell-Connectoren (HTTP/REST und MCP) | Vorgeschlagen | 2026-06-17 | — |
| [0007](./0007-wissens-interchange-format-json-bundle.md) | Wissens-Interchange-Format: JSON-Bundle mit lokalem Re-Embedding | Vorgeschlagen | 2026-06-17 | — |
| [0008](./0008-adaptives-chunking-retrieval-unterebene.md) | Adaptives Chunking als verborgene Retrieval-Unterebene | Vorgeschlagen | 2026-06-18 | — |
| [0009](./0009-hierarchisches-coarse-to-fine-retrieval.md) | Hierarchisches Coarse-to-Fine-Retrieval mit Head-Vektoren | Akzeptiert | 2026-06-19 | — |
| [0010](./0010-llm-provider-auto-extraktion.md) | LLM-Provider & Aktivierung der Auto-Extraktion (M2) | Akzeptiert | 2026-07-01 | — |
| [0011](./0011-episodisches-gedaechtnis-design.md) | Design des episodischen Agent-Gedächtnisses (M3) | Vorgeschlagen | 2026-07-01 | — |
| [0012](./0012-mandanten-rollen-modell.md) | Mandanten- und Rollen-Modell (M3) | Vorgeschlagen | 2026-07-01 | — |

*(Neue Einträge chronologisch sortiert auflisten — konsistent bleiben im Repo.)*

## Beitragen

Neue ADRs werden über den `adr-writer`-Skill erzeugt. Manuell geht auch:

1. Nächste Nummer ermitteln (höchste existierende + 1, 4-stellig).
2. Datei anlegen unter `docs/adr/<NNNN>-<slug>.md`.
3. Template aus `adr-writer/references/template.md` folgen.
4. Diesen Index um einen Eintrag erweitern.
5. Status-Wechsel nur nach den Regeln aus `adr-writer/references/status-guide.md`.
