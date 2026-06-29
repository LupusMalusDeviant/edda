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
