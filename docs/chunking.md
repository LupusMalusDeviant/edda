# Chunking (adaptive Dokument-Zerlegung)

## Zweck

Große Dokumente werden vor dem Embedding in mehrere **Chunks** zerlegt, damit die semantische Suche das
passende Teilstück eines Dokuments findet, statt ein langes Dokument auf einen unscharfen Mittel-Vektor zu
reduzieren. Das Chunking ist **adaptiv nach Dokumentstil** (Prosa, Markdown, Code, Tabellen) und läuft rein
im Hintergrund: Im Wissensgraphen bleibt jedes Dokument **ein** Knoten — Chunks sind nie sichtbar (ADR-0008).

## Datenmodell

- Dokument = `:Rule`-Knoten (unverändert; Graph-UI, Keyword-Suche, Relationen, Statistik).
- Chunks = `(:Rule)-[:HAS_CHUNK]->(:RuleChunk {ord, text, style, embedding, parentId, ownerId})`.
- Vektorindex `chunk_embeddings` liegt auf `(:RuleChunk).embedding`.
- Retrieval (`SemanticBooster`) fragt den Chunk-Index ab und aggregiert die Treffer pro **Parent-Dokument**
  (Max-Score) → der Rest der Pipeline sieht weiterhin Dokument-Treffer.
- Kleines Dokument (≤ max. Chunk-Größe) oder Chunking deaktiviert → genau ein Chunk = Verhalten wie zuvor.

Weil `:RuleChunk` ein eigenes Label trägt, taucht es in keiner Anzeige-Query auf (die alle auf `:Rule`
keyen). Nur drei eng umrissene Storage-Stellen kennen Chunks: Schreiben (`Neo4jEmbeddingCache`), Lesen
(`SemanticBooster`) und Lebenszyklus (`Neo4jKnowledgeGraph`: Delete-Cascade, Reset, Kanten-Statistik ohne
`HAS_CHUNK`).

## Adaptive Strategie

`AdaptiveDocumentChunker` (`src/AKG/Chunking/`):

1. **Stil-Erkennung** (`DocumentStyleDetector`): aus Datei-Endung (Hint) + Inhalt → Prosa | Markdown | Code | Tabelle.
2. **Block-Segmentierung** (`BlockSegmenter`): zerlegt in atomare Blöcke — eingezäunte Code-Blöcke (```` ``` ````)
   und Pipe-Tabellen bleiben am Stück, der Rest ist Text. Reine Code-Dateien werden als ein Code-Block behandelt.
3. **Packen + rekursives Splitten**:
   - **Text/Markdown**: rekursiver Splitter (LangChain/HuggingFace-`RecursiveCharacterTextSplitter`-Prinzip)
     mit stil-spezifischen Separatoren (Überschriften → Absätze → Zeilen → Sätze → Wörter) und Overlap.
   - **Code**: gleiche Mechanik mit code-orientierten Separatoren (`class`/`def`/`function`/…).
   - **Tabellen**: zeilenweise zerlegt, **Header wird in jedem Stück wiederholt** → jedes Stück bleibt eine
     valide Tabelle.

Zeichenbasiert (kein Tokenizer-Download nötig); Faustregel ~4 Zeichen ≈ 1 Token.

## Konfiguration

Live über die Web-UI (Reiter **Embeddings** → Karte *Chunking*) oder per Umgebungsvariable (Default in Klammern):

| Env | Bedeutung |
|-----|-----------|
| `CHUNKING_ENABLED` (`true`) | Chunking an/aus. Aus → ein Chunk pro Dokument. |
| `CHUNKING_MAX_CHARS` (`1200`) | Ziel-Maximalgröße eines Chunks in Zeichen. |
| `CHUNKING_OVERLAP_CHARS` (`150`) | Überlappung benachbarter Text-Chunks (verbessert Recall an Schnittstellen). |

UI-Einstellungen schlagen die Env-Werte (ADR-0004). Änderungen wirken auf neue Embeddings sofort; für bereits
gespeicherte Dokumente einmal **„Embeddings neu berechnen"** ausführen — das löscht alte Chunks und baut sie neu auf.

## Empfohlene Embedding-Modelle (self-hosting, ein Vektorraum)

Chunking nutzt **ein** Embedding-Modell für alle Dokumente (ein gemeinsamer, vergleichbarer Vektorraum). Für
einen lokalen, HuggingFace-üblichen Aufbau eignen sich offene Modelle, die über den **Ollama**- oder
**Custom**-Provider laufen — ohne CDN-/Runtime-Download im App-Prozess:

| Modell | Dim. | Stärke |
|--------|------|--------|
| `bge-m3` | 1024 | Sehr robust, mehrsprachig, lange Kontexte — guter Allrounder. |
| `nomic-embed-text` | 768 | Schnell, ressourcenschonend, solide für Code + Prosa. |
| `mxbai-embed-large` | 1024 | Starke Retrieval-Qualität (englisch-lastig). |
| `e5-large-v2` | 1024 | Bewährte Retrieval-Baseline. |

### Empfehlung nach dominantem Inhaltstyp

Da Edda **ein** gemeinsames Modell nutzt (gemeinsamer Vektorraum), wähle es nach dem vorherrschenden Inhalt
deines Korpus — nicht mehrere gleichzeitig. Das Chunking adaptiert pro Stil; das Modell bleibt eins.

| Inhaltstyp | Empfehlung | Warum |
|------------|-----------|-------|
| Prosa / gemischt | **`bge-m3`** | Mehrsprachig, robust, langer Kontext — bester Allrounder. |
| Markdown / Doku | **`bge-m3`** oder **`mxbai-embed-large`** | Struktur + Prosa; `mxbai` sehr stark (englischlastig). |
| Code | **`nomic-embed-text`** (Ollama) bzw. **`jina-embeddings-v2-base-code`** (HF/Custom) | Auf Code-/Doku-Mischung trainiert. |
| Tabellen / Datensätze | **`bge-m3`** | Verkraftet linearisierte Tabellen am besten; dedizierte Tabellen-Embedder sind selten/proprietär. |

**Gemischter Korpus (Normalfall): `bge-m3`.** Das Install-Skript zieht es daher als Default.

Das Install-Skript (`install.sh`/`install.ps1`) kann auf Wunsch einen **Ollama-Dienst im Compose-Stack**
starten (Profil `local-embeddings`) und das gewählte Modell (Default `bge-m3`) automatisch per `ollama pull`
laden — Base-URL ist dann `http://ollama:11434`. Beispiel manuell (Host-Ollama): Provider `ollama`, Modell
`bge-m3`, Base-URL `http://localhost:11434`. AWS Bedrock
(`amazon.titan-embed-text-v2:0`, Cohere) und die HTTP-Provider (OpenAI/Google/Voyage) funktionieren ebenso —
entscheidend ist, dass **ein** Modell für DB- und Query-Embedding genutzt wird.

> Hinweis: Verschiedene Modelle *pro Dokumenttyp* werden bewusst **nicht** unterstützt — getrennte Vektorräume
> sind nicht vergleichbar (ADR-0008). Die Typ-Adaption passiert im Chunking, nicht im Modell.

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/Core/Abstractions/IDocumentChunker.cs` | Vertrag (Core). |
| `src/Core/Models/DocumentChunk.cs` · `ChunkingOptions.cs` | Chunk + Tuning-Parameter. |
| `src/AKG/Chunking/AdaptiveDocumentChunker.cs` | Orchestrierung (Stil → Segmentierung → Packen). |
| `src/AKG/Chunking/DocumentStyleDetector.cs` · `BlockSegmenter.cs` · `RecursiveTextSplitter.cs` · `TableSplitter.cs` | Strategie-Bausteine. |
| `src/AKG/Chunking/ChunkingOptionsResolver.cs` | Settings + `CHUNKING_*`-Env → effektive Optionen. |
| `src/AKG/Embeddings/Neo4jEmbeddingCache.cs` | Chunkt Regelkörper, speichert `:RuleChunk` + Vektorindex. |
| `src/AKG/Context/SemanticBooster.cs` | Chunk-Retrieval → Aggregation auf Parent-Dokument. |
