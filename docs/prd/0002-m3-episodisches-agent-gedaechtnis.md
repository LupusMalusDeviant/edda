# PRD-0002: M3 — Episodisches Agent-Gedächtnis

- **Status:** Entwurf
- **Datum:** 2026-07-01
- **Autor:** LupusMalusDeviant
- **Stakeholder:** Product Owner: Repo-Eigner (LupusMalusDeviant); Consulted: angebundene Agenten (MCP)
- **Ersetzt:** —

## Problem / Motivation

Edda ist heute ein **kuratierter Wissensgraph**, kein sich entwickelndes **Gedächtnis**. Es kann keine
Konversations-Fakten erfassen („der Nutzer bevorzugt X"), sie später gezielt abrufen oder gezielt
vergessen. Cognees Kernangebot für Agenten ist genau das: `remember` / `recall` / `forget` plus
Konsolidierung von Sitzungswissen ins Langzeit-Gedächtnis. In der Kategorien-Bewertung verlor Edda die
beiden Daily-AI-Agent-Felder wesentlich deshalb.

**Do-Nothing:** Edda bleibt „nachschlagbares Regelwerk" statt „Agent, der sich an mich erinnert".
Angebundene Agenten müssen Kontext bei jeder Sitzung neu aufbauen — die „digitale Amnesie", gegen die
Edda eigentlich antritt, bleibt für die episodische Ebene ungelöst.

## Ziele

- Ein Agent kann Fakten **merken** (`remember`), **abrufen** (`recall`) und gezielt **vergessen** (`forget`).
- Sitzungs-Fakten werden ins Langzeit-Gedächtnis **konsolidiert** (nicht nur flüchtig).
- Veraltetes Gedächtnis **verblasst** automatisch (Anbindung an den Confidence-Decay aus 0.2).
- Schreibzugriff bleibt **safety-first**: `recall` ist read-only exponierbar, `remember`/`forget` sind
  Schreib-Tools und über MCP per Default gesperrt (default-deny wie `manage_*`).

## Non-Goals

- **Kein** Ersatz des kuratierten Regel-Graphen — der bleibt unverändert bestehen.
- **Keine** Mandantenfähigkeit — siehe PRD-0003.
- **Keine** LLM-Auto-Extraktion aus Rohdaten — siehe PRD-0001 (M2).
- **Kein** unbegrenztes Roh-Transkript-Logging — gemerkt wird verdichtetes Wissen, keine Volltext-Chats.

## Zielgruppen / Personas

### Daily-AI-Agent-Nutzer (privat/Team)

- Kontext: nutzt täglich einen Agenten (Claude Code/Cursor) mit Edda als Gedächtnis.
- Pain Point: der Agent „vergisst" Präferenzen/Entscheidungen zwischen Sitzungen.

### Angebundener Agent (betroffen)

- Kontext: ruft über MCP ab; soll relevante frühere Fakten in den Kontext bekommen.
- Pain Point: kein API, um Erinnerungen zu schreiben/abzurufen/zu verwerfen.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Ein `remember`-Tool speichert einen verdichteten Fakt als user-skopierten Gedächtnis-Knoten. | Must |
| FR-02 | Ein `recall`-Tool liefert zur Anfrage relevante Gedächtnis-Knoten (nutzt die bestehende Kontext-Kompilierung, auf Gedächtnis-Knoten skopiert). | Must |
| FR-03 | Ein `forget`-Tool entfernt Gedächtnis-Knoten gezielt (per ID/Kriterium). | Must |
| FR-04 | `remember`/`forget` sind Schreib-Tools und über MCP per Default gesperrt; `recall` ist read-only und (opt-in) exponierbar. | Must |
| FR-05 | Gedächtnis ist strikt user-skopiert (userId aus dem Ausführungskontext, nie aus Tool-Argumenten). | Must |
| FR-06 | Veraltetes Gedächtnis verblasst über den Confidence-Decay (0.2); sehr alte, nie wieder abgerufene Fakten sinken in der Relevanz. | Should |
| FR-07 | Sitzungsende konsolidiert die in der Sitzung gemerkten Fakten ins Langzeit-Gedächtnis (Dedup gegen Bestehendes). | Should |
| FR-08 | Beim Kontext-Abruf können relevante Gedächtnis-Fakten optional in die kompilierte Ausgabe einfließen. | Should |

## Nicht-Funktionale Anforderungen

- **Safety-first:** kein Schreibzugriff über MCP ohne explizite Freischaltung (default-deny bleibt).
- **Lokal-only:** Gedächtnis liegt lokal; kein externer Dienst nötig (kein LLM-Zwang für recall).
- **Nicht-Regression:** ohne Nutzung der Gedächtnis-Tools bleibt bestehendes Verhalten unverändert.
- **Testbar ohne Infrastruktur** (Mocks), 100 % Coverage neuer Klassen.

## User Stories / Use Cases

- **US-01:** Als Agent möchte ich einen Nutzer-Fakt merken, damit ich ihn in einer späteren Sitzung nutzen kann.
- **US-02:** Als Agent möchte ich zu einer Aufgabe relevante Erinnerungen abrufen, damit meine Antwort den Kontext kennt.
- **US-03:** Als Nutzer möchte ich eine falsche/veraltete Erinnerung vergessen lassen, damit sie nicht weiter einfließt.
- **US-04:** Als Betreiber möchte ich Schreib-Erinnerungen über MCP standardmäßig gesperrt lassen, damit fremde Agenten nichts injizieren.

## Akzeptanzkriterien / Success Metrics

- `remember`/`recall`/`forget` implementiert + mit Mocks getestet (100 % neue Klassen).
- Ein gemerkter Fakt ist in einer neuen „Sitzung" per `recall` auffindbar; nach `forget` nicht mehr.
- Über MCP sind `remember`/`forget` ohne `MCP_ALLOW_WRITE_TOOLS` **nicht** aufrufbar (default-deny verifiziert).
- Bestehende Suite bleibt ohne Gedächtnis-Nutzung unverändert grün.

## Offene Fragen

- Wie wird Gedächtnis modelliert — als user-skopierte AKG-Knoten (`SourceType=memory`) oder eigener Store? → ADR-0011.
- Was ist eine „Sitzung" und wann/wie konsolidieren (Trigger, Dedup-Strategie)? → ADR-0011.
- Soll `recall` per Default in der Allowlist stehen oder opt-in bleiben? (Vorschlag: opt-in.)

## Referenzen

- ADR-0011 — Design des episodischen Gedächtnisses (folgt)
- PRD-0003 — Mandantenfähigkeit (M3, Schwesterdokument)
- ROADMAP.md — Track 2; Confidence-Decay 0.2 (umgesetzt)

## Nächste Schritte

- ADR-0011 (Modell/Design) erstellen, dann in den M3-Implementation-Plan aufnehmen.
