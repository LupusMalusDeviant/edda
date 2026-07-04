# Skill-Log: adr-writer

## 2026-07-04 22:08 — Run

**Aufgabe:** ADR für Dataset-Permissions (M3-Folge) — Modell für teilbare Wissens-Teilmengen samt Berechtigungen, nachdem ADR-0012 „Dataset-/Domain-Berechtigungen" nur als Anforderung genannt, aber nicht konkretisiert hatte. Design-Runde + Nutzer-Entscheidung vorab getroffen.

**Entscheidungen:**
- Nummer: 0014
- Supersede: nein (konkretisiert ADR-0012, verfeinert C1/C2; ersetzt nichts)
- Datenmodell: Provenance-Gruppe (Dataset == Quelle, git:<repo>/upload:<source>) — Optionen eigener :Dataset-Knoten / Label-Tag / Status quo verworfen
- Permissions: Per-Dataset-Rolle Viewer/Editor/Owner (C2-Triade wiederverwendet)
- Status: Akzeptiert (Nutzer-Entscheidung via AskUserQuestion)
- Index `docs/adr/README.md`: aktualisiert

**Artefakte:**
- docs/adr/0014-dataset-permissions-provenance-gruppen-rollen.md
- docs/adr/README.md

**Status:** abgeschlossen

---

## 2026-07-01 15:40 — Run

**Aufgabe:** ADRs für M3 — Design episodisches Gedächtnis + Mandanten-/Rollen-Modell, plan-first.

**Entscheidungen:**
- Nummern: 0011 (episodisch: user-skopierte AKG-Knoten + dünne API + Decay), 0012 (Mandanten: Single-Graph mit Tenant-/Rollen-Scoping + Default-Tenant).
- Supersede: nein.
- Status: beide Vorgeschlagen (Review offen).
- Index `docs/adr/README.md`: aktualisiert (zwei Einträge).

**Artefakte:**
- docs/adr/0011-episodisches-gedaechtnis-design.md
- docs/adr/0012-mandanten-rollen-modell.md
- docs/adr/README.md

**Status:** abgeschlossen

---

## 2026-07-01 15:15 — Run

**Aufgabe:** ADR für M2 (Auto-Wissensgraph-Ingestion) — LLM-Provider-Default + Aktivierung der Auto-Extraktion im light-Build. Folge-Entscheidung aus ADR-0001, plan-first.

**Entscheidungen:**
- Nummer: 0010
- Supersede: nein (verfeinert ADR-0001, ersetzt es nicht)
- Provider-Default: Ollama-lokal + pluggable, Enricher opt-in im light-Build (Optionen Cloud-Default / rein-kuratiert / volle Chat-Runtime verworfen)
- Status: Vorgeschlagen (plan-first, Review durch Repo-Eigner offen)
- Index `docs/adr/README.md`: aktualisiert

**Artefakte:**
- docs/adr/0010-llm-provider-auto-extraktion.md
- docs/adr/README.md

**Status:** abgeschlossen

---

## 2026-06-19 09:03 — Run

**Aufgabe:** ADR für die Retrieval-Architektur-Umstellung — hierarchisches Coarse-to-Fine-Retrieval mit Centroid-basierten Head-Vektoren (Pre-Pruning gegen die ~19k-Node-Masse).

**Entscheidungen:**
- Nummer: 0009
- Supersede: nein (ergänzt ADR-0008 um die hierarchische Head-Stufe, ersetzt es nicht)
- Head-Vektor-Quelle: Centroid der Datei-Chunk-Embeddings (vom User gewählt; Alternativen LLM-Summary / Metadaten verworfen)
- Status: Akzeptiert (Entscheidung getroffen, Umsetzung folgt via Implementation-Plan)
- Index `docs/adr/README.md`: aktualisiert

**Artefakte:**
- docs/adr/0009-hierarchisches-coarse-to-fine-retrieval.md
- docs/adr/README.md

**Status:** abgeschlossen

---

## 2026-06-17 14:33 — Run

**Aufgabe:** Zwei ADR-Entwürfe für Plan-0002 (UI-Konfiguration) — Live-Apply der Laufzeit-Konfiguration und config-getriebenes Connector-Modell für Wissensquellen.

**Entscheidungen:**
- Nummern: 0004, 0005
- Supersede: nein (0005 generalisiert die Quellen-Abstraktion aus ADR-0001, ersetzt sie aber nicht)
- Status: Vorgeschlagen (Entwürfe zum noch nicht final freigegebenen Plan-0002)
- Index `docs/adr/README.md`: aktualisiert (zwei Einträge)

**Artefakte:**
- docs/adr/0004-live-apply-laufzeit-konfiguration.md
- docs/adr/0005-config-getriebene-wissensquellen-connectoren.md
- docs/adr/README.md

**Status:** abgeschlossen

---

## 2026-06-15 13:37 — Run

**Aufgabe:** ADR für die Git-Client-Technologie der Ingestion-Pipeline (Remote-Klon) erstellen — Folge-Entscheidung aus ADR-0001.

**Entscheidungen:**
- Nummer: 0002
- Supersede: nein
- Git-Client-Tech: LibGit2Sharp (managed), gewählt vom User
- Status: Akzeptiert
- Index `docs/adr/README.md`: aktualisiert

**Artefakte:**
- docs/adr/0002-git-client-technologie-ingestion.md
- docs/adr/README.md

**Status:** abgeschlossen

---

## 2026-06-15 12:27 — Run

**Aufgabe:** ADR für die Einführung eines optionalen LLM-Enrichers in der neuen AKG-Ingestion-Pipeline (Git/Jira/Awork → AKG-Wissensknoten) erstellen.

**Entscheidungen:**
- Nummer: erstes ADR im Repo → 0001
- Supersede: nein
- Status: Akzeptiert (Entscheidung bereits getroffen, Umsetzung folgt)
- Index `docs/adr/README.md`: neu angelegt (erstes ADR)

**Artefakte:**
- docs/adr/0001-optionaler-llm-enricher-ingestion.md
- docs/adr/README.md

**Status:** abgeschlossen

---
