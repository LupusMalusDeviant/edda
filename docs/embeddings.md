# Embeddings

Embeddings ermöglichen die semantische Phase des AKG-Retrievals (`SemanticBooster`) und werden im
`rule_embeddings`-Vektorindex (Neo4j) bzw. via App-seitigem Cosine-Fallback genutzt.

## Provider

Auswahl über `EMBEDDING_PROVIDER` (bzw. `Embeddings:Provider` in der Config):

| Wert | Service | Dimensionen | Key/Config |
|------|---------|-------------|------------|
| `openai` | OpenAiEmbeddingService | 1536 | `OPENAI_API_KEY` |
| `google` | GoogleEmbeddingService | 3072 | `GOOGLE_API_KEY` |
| `voyage` | VoyageEmbeddingService | 1024 | `VOYAGE_API_KEY` |
| `ollama` | OllamaEmbeddingService | konfigurierbar | `EMBEDDING_BASE_URL`, `EMBEDDING_MODEL`, `EMBEDDING_DIMENSIONS` |
| `custom` | CustomEmbeddingService (OpenAI-kompatibel) | konfigurierbar | `EMBEDDING_BASE_URL`, `EMBEDDING_API_KEY`, `EMBEDDING_MODEL`, `EMBEDDING_DIMENSIONS` |
| `null` (Default) | NullEmbeddingService | 0 | — (reines Keyword-Retrieval) |

Generischer Fallback-Key: `EMBEDDING_API_KEY` greift, wenn der provider-spezifische Key leer ist.

Verdrahtung: `src/Embeddings/DependencyInjection/EmbeddingServiceExtensions.cs` (`AddEmbeddingService`).

## Rebuild

Bei Provider- oder Modellwechsel müssen die Regel-Embeddings neu berechnet werden:

- **UI**: Seite `/embeddings` → „Embeddings neu berechnen" (zeigt Live-Fortschritt).
- **REST**: `POST /api/akg/embed/rebuild` (202 Accepted, läuft im Hintergrund).
- **Automatisch**: beim Start berechnet der Seed-HostedService fehlende Embeddings im Hintergrund.

Der Provider ist ein DI-Singleton (env-gesteuert) — ein Wechsel erfordert einen Neustart.

## Hinweis Dimensionen

Der Vektorindex wird auf die Dimensionen des aktiven Providers angelegt. Nach einem Wechsel der
Dimensionalität (z. B. openai→google) `embed/rebuild` ausführen, damit der Index passend neu
erstellt wird.

## Retrieval-Tuning

Die Schwellwerte und Top-K-Grenzen der Hybrid-Suche (semantisches Boosting, MMR-Reranking,
hierarchische Head-Vorfilterung) lassen sich ohne Rebuild über Umgebungsvariablen justieren. Ohne
Angabe gelten die bisherigen Defaults; das Verhalten ändert sich nur, wenn ein Wert gesetzt wird.

| Variable | Default | Wirkung |
|----------|---------|---------|
| `RETRIEVAL_SIMILARITY_THRESHOLD` | `0.5` | Minimale Cosine-Ähnlichkeit für einen semantischen Treffer; darunter verworfen. |
| `RETRIEVAL_VECTOR_TOP_K` | `100` | Anzahl der aus dem Vektorindex geholten Nachbar-Chunks. |
| `RETRIEVAL_MMR_TOP_N` | `15` | Anzahl der Top-Kandidaten, die per MMR diversifiziert werden. |
| `RETRIEVAL_MMR_LAMBDA` | `0.7` | MMR-Abwägung: `1.0` = nur Relevanz, `0.0` = nur Diversität. |
| `RETRIEVAL_HEAD_THRESHOLD` | `0.4` | Minimale Head-Zentroid-Ähnlichkeit für die Stage-1-Vorfilterung (ADR-0009). |

Verdrahtung: `RetrievalOptions` (`src/Core`) + `RetrievalOptionsResolver` (`src/AKG/Context`), gebunden
in `AkgServiceExtensions` und genutzt von `SemanticBooster` und `ContextCompiler`. Nicht-numerische
oder nicht-positive Werte fallen auf den Default zurück.
