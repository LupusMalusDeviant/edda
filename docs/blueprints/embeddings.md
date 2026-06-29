# Embeddings

## Zweck

Stellt die Vektor-Embeddings bereit, mit denen der Wissensgraph semantisch durchsucht wird — sowohl für
die gespeicherten DB-Vektoren als auch für die Query-/Prompt-Embeddings (ein gemeinsamer Vektorraum, damit
beide vergleichbar sind). Sieben austauschbare Provider (inkl. AWS Bedrock) plus eine live-konfigurierbare
„Resolving"-Fassade, die Provider/Modell/Key zur Laufzeit aus den Settings auflöst (siehe ADR-0004). Große
Dokumente werden vor dem Embedding adaptiv gechunkt; die Chunks bleiben im Graphen unsichtbar (siehe
`docs/chunking.md`, ADR-0008).

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/Embeddings/OpenAiEmbeddingService.cs` | OpenAI-Provider (`text-embedding-3-*`). |
| `src/Embeddings/GoogleEmbeddingService.cs` | Google-/Gemini-Embeddings. |
| `src/Embeddings/VoyageEmbeddingService.cs` | Voyage-Embeddings. |
| `src/Embeddings/OllamaEmbeddingService.cs` | Lokale Ollama-Embeddings. |
| `src/Embeddings/CustomEmbeddingService.cs` | Generischer OpenAI-kompatibler HTTP-Provider. |
| `src/Embeddings/BedrockEmbeddingService.cs` | AWS-Bedrock-Embeddings (Titan/Cohere via `InvokeModel`). |
| `src/Embeddings/NullEmbeddingService.cs` | No-Op (kein API-Key, reines Keyword-Retrieval). |
| `src/Embeddings/EmbeddingProviderFactory.cs` | Wählt den Provider per `EmbeddingProviderConfig`. |
| `src/Embeddings/IEmbeddingProviderFactory.cs` | Factory-Interface. |
| `src/Embeddings/ResolvingEmbeddingService.cs` | Live-Fassade: löst Provider/Modell/Key pro Aufruf aus `ISettingsService` + `ICredentialStore` auf, cached pro Schlüssel, invalidiert beim `Changed`-Event. |
| `src/Embeddings/EmbeddingProviderConfig.cs` | Provider-Konfig-Record. |
| `src/Embeddings/DependencyInjection/EmbeddingServiceExtensions.cs` | `AddEmbeddingService` (registriert Factory + ResolvingEmbeddingService als `IEmbeddingService`). |

## Abhängigkeiten

### Intern
- **Core** — `IEmbeddingService`, `ISettingsService`, `ICredentialStore`, `IIdentityContext`.

### Extern (Packages)
- `Microsoft.Extensions.Http` — `IHttpClientFactory` für die HTTP-Provider.
- DI-/Config-/Logging-Abstractions.

## Öffentliche API / Interface

```csharp
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    int Dimensions { get; }
    bool IsAvailable { get; }
}
```

`AddEmbeddingService(IConfiguration)` registriert die Resolving-Fassade als `IEmbeddingService`. Schlüssel
im Credential-Store: `{userId}:embed:{provider}`; Env-Fallback `EMBEDDING_PROVIDER` / `EMBEDDING_MODEL` /
`EMBEDDING_API_KEY`.

## Datenfluss / Call-Flow

1. Konsument (z. B. `Neo4jEmbeddingCache` oder die Kontext-Kompilierung) ruft `IEmbeddingService.EmbedAsync`.
2. `ResolvingEmbeddingService` liest Provider/Modell/Dimensionen aus `ISettingsService.Current.Embedding`
   (Fallback: `EMBEDDING_*`-Env), holt den Key aus dem Credential-Store.
3. Cached die konkrete Provider-Instanz über einen Schlüssel (Provider+Modell+URL+Dim+KeyHash); bei
   `Changed` wird der Cache invalidiert.
4. Delegiert `EmbedAsync` an den gebauten Provider (HTTP-Call bzw. Null).

## Offene Fragen / TODOs

- `input_type` document/query (echte Index-/Query-Asymmetrie) sowie Vektorindex-Drop/Recreate bei
  Dimensionswechsel sind bewusst offen; aktuell greift nach einem Provider-Wechsel der App-seitige
  Cosine-Fallback (korrekt, aber langsamer). Der Vektorindex liegt seit ADR-0008 als `chunk_embeddings`
  über `(:RuleChunk)`.
