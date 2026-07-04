# ADR-0014: Dataset-Permissions über Provenance-Gruppen mit Per-Dataset-Rollen

- **Status:** Akzeptiert
- **Datum:** 2026-07-04
- **Autor:** LupusMalusDeviant
- **Konsultiert:** —

## Kontext und Problemstellung

ADR-0012 hat Mandantenfähigkeit als Single-Graph-Scoping beschlossen und dabei „Dataset-/Domain-
Berechtigungen" bereits als Anforderung (PRD-0003, FR-01..04) genannt — aber nur die *Isolations-
Architektur* entschieden, nicht das konkrete *Dataset-Berechtigungsmodell*. Genau diese offene Konkretion
holt dieses ADR nach.

Heute kennt Edda pro Tenant nur **zwei** Sichtbarkeits-Buckets für Regeln: tenant-global (Regel-Property
`ownerId IS NULL`, für alle im Tenant sichtbar) oder rein privat (`ownerId = aktueller User`). Das Read-
Prädikat ist einheitlich in `CypherGraphStore` **und** `InMemoryCypherExecutor`:
`(r.ownerId IS NULL OR r.ownerId = $userId) AND coalesce(r.tenantId,'default') = $tenantId`. Schreibzugriffe
laufen zentral über `RuleAuthorizer` (C2, ADR-0012, Rollen Viewer/Editor/Owner + Admin). Es fehlt der
**Mittelweg**: eine *benannte* Teilmenge von Regeln (etwa ein ingestiertes Git-Repo oder ein Upload) mit
einer *Teilmenge* von Nutzern zu teilen — weder „nur ich" noch „alle im Tenant".

Die dafür nötige Gruppierungsstruktur existiert bereits: Provenance ist erstklassig modelliert — Id-Prefixe
`git:<repo>:<path>` und `upload:<source>:<file>`, synthetische Kopfknoten `git:<repo>` bzw. `upload:<source>`
und Wurzeln `git-knowledge`/`uploads`; `Neo4jKnowledgeGraph` mappt Quellnamen sogar schon auf Prefixe
(`"uploads" => "upload:"`). Regeln tragen außerdem eine `Domain` (semantisch, quer-schneidend) und freie
`Tags` — aber nur `ownerId` (+ `tenantId`) sind heute ACL-relevant.

**Kernfrage:** Wie modellieren wir teilbare Wissens-Teilmengen (Datasets) samt Berechtigungen, ohne die
verhaltensneutrale Default-/Single-User-Semantik zu brechen und ohne ein Parallel-Vokabular neben den
bestehenden C2-Rollen einzuführen?

## Anforderungen

### Funktional

- Eine benannte, teilbare Regel-Teilmenge (ein „Dataset").
- Teilen mit einzelnen Nutzern in abgestuften Rechten (lesen / ändern / weitergeben).
- Enforcement auf **Reads und Writes**; für Nicht-Berechtigte ist ein fremdes Dataset **unsichtbar**
  (nicht bloß schreibgeschützt).

### Nicht-Funktional

- **Verhaltensneutral** für Default-Tenant/Single-User (bit-identisch zum Status quo).
- **Ein Enforcement-Punkt je Richtung** — kein dritter Seam neben Read-Prädikat und `RuleAuthorizer`.
- Konsistent mit C1 (Tenant-Isolation) und C2 (Rollen-Matrix, ADR-0012).
- Testbar ohne Infrastruktur; der `InMemoryCypherExecutor` deckt dieselbe Semantik wie Neo4j.
- Keine Magic Strings; Interface-First (Regel 1, ADR-0003).

## Betrachtete Optionen

### Option 0: Provenance-Gruppe (Dataset == Quelle)

Ein Dataset **ist** eine bestehende Quelle, identifiziert über den vorhandenen Provenance-Kopfknoten bzw.
Id-Prefix (`git:<repo>`, `upload:<source>`). Kein neuer Knotentyp; die Zugehörigkeit einer Regel ergibt sich
aus ihrem Id-Prefix / ihrer Anbindung an den Kopfknoten; die ACL (Rollen-Grants) hängt am Kopfknoten.

**Positiv:**
- Minimale neue Fläche; deckt sich exakt mit der bereits vorhandenen Ingestion-/Graph-Struktur.
- Natürliche Migration: jede ingestierte Quelle ist automatisch ein Dataset.
- Der Kopfknoten trägt bereits Head-Vektoren/Retrieval (ADR-0009) — ein etablierter Aufhängepunkt.
- Wiederverwendung der C2-Rollen statt eines zweiten Berechtigungs-Vokabulars.

**Negativ:**
- Handgeschriebene Einzelregeln ohne Quell-Prefix sind zunächst keine Datasets.
- Dataset == Quelle ist starr: einzelne Regeln lassen sich nicht zwischen Datasets umgruppieren.

### Option 1: Eigener `:Dataset`-Knoten mit expliziter Mitgliedschaft

Ein neuer Knotentyp mit expliziter Mitgliedschaft (`datasetId`-Property bzw. `BELONGS_TO`-Kante), entkoppelt
von der Provenance.

**Positiv:**
- Maximal flexibel — beliebige, gemischte, auch handgeschriebene Regelmengen; Regeln umgruppierbar.

**Negativ:**
- Neuer Knotentyp + Mitgliedschaftspflege + Migration bestehender Daten.
- Zweite Gruppierungs-Wahrheit neben der Provenance-Struktur; mehr Query-Fläche in **beiden** Executors.

### Option 2: Label/Tag-basiert

Ein Dataset ist ein reservierter Tag auf den Regeln.

**Positiv:**
- Kein Schema-Change, sofort verfügbar.

**Negativ:**
- Tags sind frei editierbar → schwache Sicherheitsgarantie; ein Editor könnte sich selbst „hineintaggen".
- Kein sauberes ACL-Zuhause; kollidiert mit dem bestehenden freien Tag-Gebrauch.

### Option 3: Status quo (nichts tun)

Bei den zwei Achsen `ownerId`/`tenantId` bleiben.

**Positiv:**
- Kein Aufwand, kein Risiko.

**Negativ:**
- Die Teilen-Lücke bleibt; für Mehr-Nutzer-Tenants unzureichend — die in ADR-0012 genannte Anforderung
  bleibt unerfüllt.

## Vorschlag des Autors

Option 0. Sie erfüllt die Kernanforderung (benannte, teilbare Teilmenge mit abgestuften Rechten)
verhaltensneutral und mit der kleinsten konsistenten Erweiterung: Die Provenance-Struktur, die es ohnehin
gibt, wird zum Dataset, und die C2-Rollen werden pro Dataset wiederverwendet statt dupliziert. Der Preis —
Dataset == Quelle ist starr — ist für die häufigste Realität (man teilt genau das, was aus einer Quelle
ingestiert wurde) akzeptabel; der flexiblere `:Dataset`-Knoten (Option 1) bleibt als sauberer Folge-Schritt
möglich, sobald ein echter Bedarf an quer-geschnittenen Datasets auftritt.

## Entscheidung

**Gewählte Option:** „Provenance-Gruppe (Dataset == Quelle)"

Ein Dataset ist eine Provenance-Gruppe (Quell-Kopfknoten `git:<repo>` / `upload:<source>`); Berechtigungen
sind Per-Dataset-Rollen, die die vorhandene C2-Triade **Viewer/Editor/Owner** wiederverwenden (Viewer =
lesen, Editor = Regeln der Quelle ändern, Owner = Rollen vergeben / „share"). Ausschlaggebend: minimale
Disruption, Wiederverwendung von Provenance + C2, Verhaltensneutralität. Bewusst in Kauf genommen: die
Starrheit Dataset == Quelle.

## Konsequenzen

### Positiv

- Schließt die Teilen-Lücke zwischen „privat" und „tenant-global" — die in ADR-0012 genannte Dataset-
  Berechtigung wird real.
- Wiederverwendet Provenance-Struktur und C2-Rollen; kein Parallel-Vokabular.
- Verhaltensneutral: Regeln ohne eingeschränktes Dataset behalten die heutige `ownerId`/`tenantId`-Semantik
  bit-identisch; bestehende ingestierte Quellen sind zunächst ACL-frei und verhalten sich wie bisher, bis ein
  Owner aktiv teilt.
- Ein Enforcement-Punkt je Richtung (Read-Prädikat + `RuleAuthorizer`), testbar im In-Memory-Modus.

### Negativ

- Dataset == Quelle ist starr: kein Umgruppieren einzelner Regeln, keine Datasets für handgeschriebene
  Einzelregeln ohne Prefix — bei echtem Bedarf ist ein Folge-ADR Richtung Option 1 nötig.
- Die Menge der sichtbaren Datasets muss je Request bestimmt (und sinnvoll gecacht) werden → leichte
  Read-Pfad-Kosten.
- Rollen-Grants brauchen einen kleinen Persistenz-Ort am Kopfknoten (Property/Seiten-Tabelle).

### Folge-Entscheidungen

- Konkrete Persistenz der Dataset-Rollen-Grants (Property am Kopfknoten vs. separater Grant-Store) — erste
  Umsetzungsscheibe.
- Bestimmung/Caching der „sichtbaren Datasets" je Identity im Read-Pfad — erste Umsetzungsscheibe.
- Falls quer-geschnittene Datasets nötig werden: Folge-ADR Richtung eigener `:Dataset`-Knoten (Option 1).

### Review

**Reality-Check geplant für:** 2026-08-30 (nach der ersten Enforcement-Scheibe).

## Weitere Informationen

### Scope

Betrifft `src/Core/Abstractions` (Dataset-Rollen-Vertrag), `src/AKG/Graph` (`CypherGraphStore` +
`InMemoryCypherExecutor` Read-Prädikat) und `src/AKG/Authorization` (`RuleAuthorizer` Write-Check). Die
erste Umsetzungsscheibe — getrennt von diesem ADR — ist das **verhaltensneutrale Read-Enforcement** mit
leerer/abwesender Dataset-ACL. `IKnowledgeGraph`, TDK, Memory und MCP bleiben funktional unverändert.

### Referenzen

- [ADR-0012](./0012-mandanten-rollen-modell.md) — Mandanten-/Rollen-Modell (führt „Dataset-/Domain-
  Berechtigungen" als Anforderung ein; dieses ADR konkretisiert das Dataset-Modell)
- [ADR-0003](./0003-interface-first-fuer-injizierte-services.md) — Interface-First für injizierte Services
- [ADR-0009](./0009-hierarchisches-coarse-to-fine-retrieval.md) — Head-Vektoren (Kopfknoten als ACL-Aufhängepunkt)
- [ADR-0013](./0013-modulares-provider-framework-pluggable-persistenz.md) — Persistenz-Naht (`IGraphStore`)
- `docs/prd/0003-m3-mandantenfaehigkeit.md`; `ROADMAP.md` — Track 3
