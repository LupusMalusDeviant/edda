---
Status: Vorgeschlagen
Datum: 2026-06-17
Autor: Eric Lenk
Konsultiert: —
---

# Wissens-Interchange-Format: JSON-Bundle mit lokalem Re-Embedding

Ersetzt: —

## Kontext und Problemstellung

Es soll möglich sein, komplette Wissensdatenbanken aus anderen Projekten/Tools zu importieren und Wissen
zwischen Edda-Instanzen zu übertragen. Dabei ist entscheidend: **Embeddings sind modell-spezifisch** — ein
Vektor aus OpenAI/Voyage/Ollama ist im Vektorraum eines anderen Modells bedeutungslos.

**Kernfrage:** Welches Format überträgt Wissen verlustfrei zwischen Instanzen, und wie gehen wir mit
gespeicherten Vektoren aus Fremdsystemen um?

## Anforderungen

### Funktional
- Verlustfreier Edda↔Edda-Transfer inklusive Regel-Relationen.
- Import diverser Fremdformate: Markdown-Sammlungen, PDF, HTML (Confluence/Notion), CSV/JSONL, JSON-Arrays, Vektor-DB-Dumps.

### Nicht-funktional
- Keine Abhängigkeit vom Embedding-Modell der Quelle.
- Ausreichend mensch-lesbar/diffbar.
- Erweiterbar (Schema-Version), ein einziger Importpfad.

## Betrachtete Optionen

### Option 1: Kanonisches JSON-Bundle + lokales Re-Embedding
Ein `KnowledgeBundle` (Liste `KnowledgeRule` inkl. Relationen) als Export/Import-Format; Vektoren werden
nie übernommen, sondern nach dem Import lokal neu berechnet. Fremdformate werden auf Text/Items abgebildet
und über denselben Mapper verbunden.

**Positiv:**
- Verlustfreier Transfer der Regeln + Relationen.
- Garantiert konsistenter Vektorraum (immer das aktuell konfigurierte Embedding-Modell).
- Ein Importpfad für alle Formate.

**Negativ:**
- Re-Embedding nach dem Import kostet Zeit/Provider-Aufrufe.
- Bundle-JSON ist an das `KnowledgeRule`-Schema gekoppelt → `SchemaVersion` nötig.

### Option 2: Fremd-Vektoren mitimportieren
Die gespeicherten Embeddings aus dem Quellsystem übernehmen.

**Positiv:**
- Kein Re-Embedding nötig.

**Negativ:**
- Fachlich falsch: fremde Vektoren sind im lokalen Modellraum bedeutungslos → unbrauchbare Retrieval-Ergebnisse.

### Option 3: Nur Markdown
Import/Export ausschließlich als Markdown-Dateien.

**Positiv:**
- Maximal einfach und lesbar.

**Negativ:**
- Verliert strukturierte Relationen und Metadaten beim Roundtrip.

## Entscheidung

Gewählt wird **Option 1**: das JSON-`KnowledgeBundle` ist das kanonische Im-/Exportformat (Regeln +
Relationen, `SchemaVersion`). **Vektoren werden nie importiert**, sondern lokal neu berechnet. Fremdformate
(Markdown/ZIP/PDF/HTML/CSV/JSONL/JSON-Array, inkl. Vektor-DB-Dumps) werden auf Text/Items abgebildet und
über denselben Ingestion-Mapper zu einem verbundenen Teilgraphen verknüpft.

## Konsequenzen

### Positiv
- Verlustfreier Edda↔Edda-Transfer; Fremdsysteme über Adapter abgedeckt.
- Retrieval bleibt korrekt, weil alle Vektoren aus demselben Modell stammen.
- Vektor-DB-Dumps sind nutzbar (Text/Payload übernommen), ohne den falschen Vektorraum zu erben.

### Negativ
- Nach größeren Importen ist ein Re-Embedding-Lauf nötig (Zeit/Kosten).
- Das generische Datensatz-Mapping (Feldnamen id/title/body/…) ist heuristisch und trifft nicht jede Quelle exakt.

## Weitere Informationen

Knüpft an ADR-0004 (live konfigurierbares Embedding) an: Nach dem Import bzw. Embedding-Wechsel greift der
Re-Embed-Lauf. Export-Endpoint: `GET /api/knowledge/export`; Import über den Reiter „Wissen importieren".
