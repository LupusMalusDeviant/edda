# ADR-0012: Mandanten- und Rollen-Modell (M3)

- **Status:** Vorgeschlagen
- **Datum:** 2026-07-01
- **Autor:** LupusMalusDeviant
- **Konsultiert:** —

## Kontext und Problemstellung

M3 (PRD-0003) führt Mandantenfähigkeit ein: Tenants, Rollen (Owner/Editor/Viewer) und Dataset-/Domain-
Berechtigungen. Edda hat heute nur `ownerId`-Scoping (global vs. user-eigen) und einen zentralen
`IIdentityContext`. Die Frage ist die Isolations-Architektur — logisch im bestehenden Graph vs. physisch
getrennt vs. Namespaces eines Multi-Model-Backends.

**Kernfrage:** Wie wird Mandantenfähigkeit umgesetzt — Single-Graph mit `(tenantId, Rollen,
Permissions)`-Scoping, per-Tenant-Datenbank oder Namespace-Modell — ohne den local-first-Charakter und die
Rückwärtskompatibilität zu verlieren?

## Anforderungen

### Funktional

- Tenant-Zuordnung je Ressource; Membership mit Rolle; Enforcement in Graph + API + MCP (PRD-0003 FR-01..04).
- Rückwärtskompatibler Default-Tenant (FR-05).

### Nicht-Funktional

- Vollständige logische Isolation als Default; kein Zugriff ohne gültigen Tenant-Kontext.
- Kein signifikanter Retrieval-Overhead (Filter auf Query-Ebene).
- Testbar mit Mocks; Interface-First (Regel 1, ADR-0003).

## Betrachtete Optionen

### Option 0: Single-Graph mit Tenant-/Rollen-Scoping

`ownerId` wird um `tenantId` + Rollen + Dataset-Permissions erweitert; Enforcement zentral über
`IIdentityContext` + Query-Filter; ein Default-Tenant bildet den heutigen Single-User-Betrieb ab.

**Positiv:**
- Geringste Disruption; nutzt bestehendes Scoping + `IIdentityContext`.
- Rückwärtskompatibel (Default-Tenant); ein Speicher, keine Infra-Änderung.
- Passt zum local-first-Ethos (keine DB-Multiplikation).

**Negativ:**
- Nur **logische**, keine physische Isolation — Enforcement muss überall konsequent greifen (querschnittlich, testintensiv).
- Ein Bug im Filter kann Tenant-Grenzen verletzen — hohe Test-/Review-Anforderung.

### Option 1: Per-Tenant separate Datenbank/Graph

Jeder Tenant bekommt einen eigenen Graph/DB (physische Isolation, wie Cognees per-Tenant-Option).

**Positiv:**
- Stärkste Isolation; ein Filter-Bug kann keine Fremddaten offenlegen.

**Negativ:**
- DB-Multiplikation widerspricht local-first/Zero-Infra; Betrieb/Provisioning aufwändig.
- Cross-Tenant-Global-Wissen (geteilte Regeln) wird kompliziert.

### Option 2: Namespace-Modell (SurrealDB-Stil)

Tenants als Namespaces eines Multi-Model-Backends.

**Positiv:**
- Native Isolation + RBAC des Backends.

**Negativ:**
- Setzt einen Backend-Wechsel (z. B. SurrealDB) voraus — eigene, große Entscheidung; nicht Neo4j/memory.

## Vorschlag des Autors

Option 0 trifft die Anforderungen am besten: Sie liefert die geforderte (logische) Isolation + Rollen mit
minimaler Disruption, bleibt rückwärtskompatibel (Default-Tenant) und passt zum local-first-Ethos. Der
Preis — nur logische Isolation und querschnittliches Enforcement — wird durch zentrale Durchsetzung
(`IIdentityContext` + Query-Filter) und intensive Isolationstests beherrschbar. Physische Isolation
(Option 1) oder ein Backend-Wechsel (Option 2) sind für die Zielgröße überdimensioniert.

## Entscheidung

**Gewählte Option:** „Single-Graph mit Tenant-/Rollen-Scoping"

Ausschlaggebend: minimale Disruption, Rückwärtskompatibilität und local-first-Fit. Bewusst akzeptiert:
nur logische Isolation mit entsprechend hoher Test-/Review-Pflicht am zentralen Enforcement.

## Konsequenzen

### Positiv

- Enterprise-Isolation + Rollen ohne Infra-Multiplikation; heutiger Betrieb läuft via Default-Tenant weiter.
- Zentrale Durchsetzung an einer Stelle (`IIdentityContext` + Filter) statt verstreuter Checks.

### Negativ

- Logische (nicht physische) Isolation — dokumentierte Grenze; jeder Lese-/Schreibpfad muss den Filter honorieren.
- Höhere Test-Matrix: Isolations- und Rollen-Enforcement querschnittlich abdecken.

### Folge-Entscheidungen

- Transport des Tenant-Kontexts (Auth-Claim/Header) — Hosting-Auth, M3-Plan.
- Migrationsweg bestehender `ownerId`-Daten in den Default-Tenant — M3-Plan.

### Review

**Reality-Check geplant für:** 2026-08-30

## Weitere Informationen

### Scope

Betrifft `src/Core` (Tenant/Rollen-Verträge), `src/AKG` + Stores (Scoping-Filter), `src/Edda.Hosting`
(Auth/Enforcement) und die MCP-Exposition. Kein Backend-Wechsel.

### Referenzen

- `docs/prd/0003-m3-mandantenfaehigkeit.md`
- ADR-0003 — Interface-First; ROADMAP.md — Track 3
