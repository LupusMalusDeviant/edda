---
Status: Vorgeschlagen
Datum: 2026-06-18
Autor: Eric Lenk
Konsultiert: —
---

# Adaptives Chunking als verborgene Retrieval-Unterebene

Ersetzt: —

## Kontext und Problemstellung

Große Dokumente (lange Markdown-Seiten, Code-Dateien, breite Tabellen) wurden bisher als **ein** Vektor pro
Dokument eingebettet (`r.embedding` am `:Rule`-Knoten). Das verwässert die semantische Suche: ein einzelner
Vektor mittelt ein langes, thematisch gemischtes Dokument zu einem unscharfen Punkt, und sehr lange Texte
überschreiten die Eingabegrenzen der Embedding-Modelle.

Gewünscht ist Chunking großer Dokumente — adaptiv nach Dokumentstil (Prosa, Markdown, Code, Tabellen).
Zentrale Einschränkung des Auftraggebers: **Chunks dürfen NICHT im Wissensgraphen erscheinen.** Der Graph
soll weiterhin die verfügbaren *Dokumente* zeigen; Chunking ist reine Hintergrund-Mechanik fürs Retrieval.

**Kernfrage:** Wie führen wir Chunk-basiertes Embedding/Retrieval ein, ohne die Dokument-Ebene des Graphen
(UI, Keyword-Suche, Relationen, Statistik) zu verändern?

## Anforderungen

### Funktional
- Große Dokumente werden vor dem Embedding in mehrere Chunks zerlegt; Retrieval findet das Dokument über
  seinen besten Chunk.
- Adaptive Strategie pro Stil: Code-Blöcke und Tabellen bleiben intakt, Prosa/Markdown wird an natürlichen
  Grenzen geteilt.
- Wissensgraph (Cytoscape-UI, `knowledge_list_rules`, Statistik) zeigt weiterhin ausschließlich Dokumente.

### Nicht-funktional
- Ein gemeinsamer Vektorraum (ein Embedding-Modell) — keine modellgemischten, unvergleichbaren Vektoren.
- Deterministisch, ohne neue schwere Abhängigkeit (kein Tokenizer-/Modell-Download; self-hosting bleibt gewahrt).
- Kleine Dokumente verhalten sich wie zuvor (genau ein Chunk).
- Lebenszyklus konsistent: Löschen/Reset/Re-Embed räumen Chunks mit auf.

## Betrachtete Optionen

### Option 1: Verborgene `:RuleChunk`-Unterebene (gewählt)
Jede Regel bekommt Kindknoten `(:Rule)-[:HAS_CHUNK]->(:RuleChunk {ord, text, style, embedding, parentId})`
mit eigenem Label. Der Vektorindex liegt auf `(:RuleChunk).embedding`; Retrieval fragt Chunks ab und gibt
die **Parent-Dokument-ID** zurück (Max-Aggregation pro Dokument). Adaptives Chunking per `IDocumentChunker`
(Stil-Erkennung + block-basierter, rekursiver Splitter), ein gemeinsames Embedding-Modell.

**Positiv:**
- Graph-/UI-/Keyword-/Statistik-Pfad bleibt unverändert, weil alles auf `:Rule` keyt — `:RuleChunk` ist ein
  anderes Label und taucht in keiner Anzeige-Query auf.
- Standard-Muster „Parent-Document-Retrieval": Chunks indizieren, Dokumente liefern.
- Embeddings werden nur an zwei Stellen berührt (Cache-Schreiben, Booster-Lesen) → kleine, lokale Änderung.

**Negativ:**
- Mehr Knoten/Schreibvorgänge pro Dokument; Lebenszyklus (Delete/Reset) muss Chunks mit aufräumen.
- Eine label-agnostische Query (Kanten-Statistik) muss `HAS_CHUNK` explizit ausschließen.

### Option 2: Chunks als vollwertige Graph-Knoten
Chunks als eigene `:Rule`-artige Knoten mit Relation zum Dokument.

**Positiv:**
- Einheitliches Knotenmodell, kein Sonder-Label.

**Negativ:**
- Verletzt die Kern-Einschränkung: Chunks würden im Graphen, in der Keyword-Suche und in der Statistik
  auftauchen und müssten überall wieder herausgefiltert werden — fehleranfällig.

### Option 3: Status quo — ein Vektor pro Dokument
Kein Chunking.

**Positiv:**
- Nichts zu ändern.

**Negativ:**
- Schlechtes Retrieval bei großen/gemischten Dokumenten; sehr lange Texte sprengen die Modell-Grenzen.

### Verworfen: verschiedene Embedding-Modelle pro Dokumenttyp
Erwogen wurde, je Dokumenttyp ein eigenes Modell zu nutzen. Verworfen, weil getrennte Vektorräume nicht
vergleichbar sind (Kosinus über Räume hinweg ist bedeutungslos) und mehrere Indizes plus Query-gegen-alle den
Aufwand stark erhöhen. Entscheidung: **adaptives Chunking, aber ein gemeinsames Modell.**

## Entscheidung

Gewählt wird **Option 1**: Chunks leben als verborgene `:RuleChunk`-Kindknoten, der Vektorindex
(`chunk_embeddings`) liegt auf den Chunks, und Retrieval mappt Treffer auf die Parent-Dokument-ID zurück.
Gechunkt wird adaptiv nach Stil mit einem gemeinsamen Embedding-Modell; kleine Dokumente ergeben genau einen
Chunk (Verhalten wie bisher). Der Wissensgraph bleibt strikt dokument-basiert.

## Konsequenzen

### Positiv
- Bessere semantische Treffer bei großen/gemischten Dokumenten, ohne die Graph-Darstellung zu verändern.
- Code-Blöcke und Tabellen bleiben als Einheit erhalten (Tabellen mit wiederholtem Header pro Stück).
- Ein Vektorraum → Retrieval bleibt korrekt; kein Modell-Download → self-hosting bleibt gewahrt.

### Negativ
- Höhere Schreib-/Speicherkosten pro Dokument (mehrere Chunk-Knoten + Vektoren).
- Ein Embedding-Dimensionswechsel erfordert weiterhin einen Index-Neuaufbau (offener Punkt, vgl. Feature
  *Embeddings*), jetzt auf `chunk_embeddings`.

## Weitere Informationen

Knüpft an ADR-0004 (live konfigurierbares Embedding) an: Chunk-Größe/Overlap sind live über die Web-UI bzw.
`CHUNKING_*`-Env konfigurierbar; nach Änderungen greift der Re-Embed-Lauf. Implementierung unter
`src/AKG/Chunking/` (`AdaptiveDocumentChunker`), Speicherung in `Neo4jEmbeddingCache`, Retrieval im
`SemanticBooster`. Modell-Empfehlungen und Konfiguration: `docs/chunking.md`.
