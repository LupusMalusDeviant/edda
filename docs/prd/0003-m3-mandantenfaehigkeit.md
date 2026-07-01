# PRD-0003: M3 — Mandantenfähigkeit

- **Status:** Entwurf
- **Datum:** 2026-07-01
- **Autor:** LupusMalusDeviant
- **Stakeholder:** Product Owner: Repo-Eigner (LupusMalusDeviant); Consulted: Enterprise-Betreiber
- **Ersetzt:** —

## Problem / Motivation

Edda kennt heute nur **User-Scoping** (`ownerId`: global vs. user-eigen). Für den **Firmen-Einsatz**
fehlt Mandantenfähigkeit: Organisationen/Teams mit isolierten Daten, Rollen (wer darf lesen/schreiben/
teilen) und Berechtigungen auf Dataset-/Domain-Ebene. Cognee (EBAC, Tenants/Rollen, per-Tenant-Isolation)
und SurrealDB (Namespaces/RBAC) haben das; in der Bewertung verlor Edda die Firmen-Kategorien u. a. hier.

**Do-Nothing:** Edda bleibt ein Single-User-/Ein-Team-Werkzeug. Mehrere Teams/Kunden auf einer Instanz
sind ohne Datentrennung nicht sicher betreibbar — der Enterprise-Einsatz bleibt versperrt.

## Ziele

- **Mandanten-Isolation:** Daten eines Tenants sind für andere Tenants unsichtbar (Graph + API + MCP).
- **Rollen:** mindestens Owner / Editor / Viewer je Tenant.
- **Berechtigungen** auf Dataset-/Domain-Ebene (read / write / share).
- **Rückwärtskompatibel:** der heutige Single-User-Betrieb funktioniert unverändert (Default-Tenant).

## Non-Goals

- **Kein** episodisches Gedächtnis — siehe PRD-0002.
- **Kein** Billing / SSO / IdP-Föderation — reine Autorisierung, keine Identitäts-Infrastruktur.
- **Keine** physische Trennung per Tenant-eigener Datenbank — Ziel ist logische Isolation im bestehenden Graph.
- **Keine** UI-Rechteverwaltung als Erstausbau (API/Config zuerst; UI später).

## Zielgruppen / Personas

### Enterprise-Admin (Tenant-Owner)

- Kontext: betreibt Edda für mehrere Teams; legt Tenants/Rollen an.
- Pain Point: heute keine Datentrennung → keine sichere Mehr-Team-Instanz.

### Team-Mitglied (Editor)

- Kontext: pflegt Wissen im eigenen Tenant/Dataset.
- Pain Point: kann heute nicht auf ein Team-Scope beschränkt schreiben.

### Viewer

- Kontext: liest Wissen, darf nicht ändern.
- Pain Point: keine Read-only-Rolle vorhanden.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Es gibt eine Tenant-Entität; jeder Wissensknoten/jede Ressource ist genau einem Tenant zugeordnet. | Must |
| FR-02 | Nutzer sind Tenants mit einer Rolle (Owner/Editor/Viewer) zugeordnet (Membership). | Must |
| FR-03 | Alle Lese-/Schreibpfade (Graph-Queries, `/api/*`, MCP-Tools) filtern/prüfen nach Tenant + Rolle. | Must |
| FR-04 | Zugriff über Tenant-Grenzen hinweg ist ausgeschlossen (vollständige logische Isolation). | Must |
| FR-05 | Der heutige Single-User-Betrieb bildet auf einen Default-Tenant ab und bleibt unverändert nutzbar. | Must |
| FR-06 | Berechtigungen auf Dataset-/Domain-Ebene: read / write / share. | Should |
| FR-07 | Admin-API zum Verwalten von Tenants, Memberships und Rollen. | Should |
| FR-08 | Audit-Log (bestehend, HMAC/Merkle) hält tenant- und rollenbezogene Schreibzugriffe fest. | Could |

## Nicht-Funktionale Anforderungen

- **Sicherheit:** Isolation ist Default; ein fehlender/ungültiger Tenant-Kontext gewährt keinen Zugriff.
- **Rückwärtskompatibilität:** bestehende Daten wandern verlustfrei in den Default-Tenant.
- **Testbarkeit:** Isolations- und Rollen-Enforcement mit Mocks vollständig testbar (100 % neue Klassen).
- **Performance:** Tenant-Filter darf Retrieval nicht signifikant verlangsamen (Filter auf Query-Ebene).

## User Stories / Use Cases

- **US-01:** Als Admin möchte ich einen Tenant + Mitglieder anlegen, damit Teams isoliert arbeiten.
- **US-02:** Als Editor möchte ich nur im eigenen Tenant schreiben, damit ich keine fremden Daten berühre.
- **US-03:** Als Viewer möchte ich lesen, aber nichts ändern können.
- **US-04:** Als Betreiber möchte ich, dass mein bisheriger Single-User-Betrieb ohne Migration weiterläuft.

## Akzeptanzkriterien / Success Metrics

- Datentrennung: ein Tenant-B-Nutzer sieht/ändert **keine** Tenant-A-Daten (Isolationstest grün).
- Rollen-Enforcement: Viewer-Schreibversuch wird abgelehnt; Editor-Schreiben nur im eigenen Tenant.
- Rückwärtskompatibilität: bestehende Tests + Single-User-Pfad bleiben grün (Default-Tenant).
- Alle FR-Must implementiert + mit Mocks testabgedeckt.

## Offene Fragen

- Modell: Single-Graph mit `(tenantId, ownerId, Rolle, Permissions)` vs. per-Tenant-DB vs. Namespaces? → ADR-0012.
- Wie wird der Tenant-Kontext transportiert (Auth-Claim, Header)? → ADR-0012 / Hosting-Auth.
- Migrationsweg bestehender `ownerId`-Daten in den Default-Tenant?

## Referenzen

- ADR-0012 — Mandanten-/Rollen-Modell (folgt)
- PRD-0002 — Episodisches Gedächtnis (M3, Schwesterdokument)
- ADR-0003 — Interface-First (für zentrale Enforcement-Abstraktion)
- ROADMAP.md — Track 3

## Nächste Schritte

- ADR-0012 (Modell) erstellen, dann in den M3-Implementation-Plan aufnehmen.
