# Core — Verträge (Interfaces & Modelle)

## Zweck

`Edda.Core` ist die Vertrags-Schicht: ausschließlich **Interfaces** (`Abstractions/`) und **Daten-Modelle**
(`Models/`), die alle übrigen Projekte teilen. Erzwingt die „Interface-First"-Regel — jede von außen
genutzte Implementierung hat hier ihr Interface. Enthält keine Laufzeitlogik (Ausnahme: `Benchmark/RetrievalMetrics`, reine Berechnung). Core ist eine Ganzkopie der Core-Schicht aus dem Edda-Monorepo;
**nur eine Teilmenge der Verträge ist in dieser Auskopplung implementiert** — die Chat-, Multi-Agent-,
Channel- und Workflow-Verträge sind mitkopiert, aber ohne Implementierung.

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/Core/Abstractions/*.cs` | ~85 Interfaces. In Edda aktiv: siehe „Öffentliche API". |
| `src/Core/Models/*.cs` | ~70 DTOs/Records. In Edda aktiv: u. a. `KnowledgeRule`, `EddaSettings`, `IngestionModels`, `ConnectorModels`, `KnowledgeBundle`, `GraphStats`, `ToolDefinition`/`ToolCall`/`ToolResult`, `TaskContext`, `ContextResult`, `TdkResult`, `AuditEvent`. |
| `src/Core/Models/EddaSettings.cs` | Laufzeit-Konfigmodell (General, LlmEnrichment, Embedding, Sources, Mcp). |
| `src/Core/Models/KnowledgeRule.cs` | Zentrales Graph-Knotenmodell (Typ, Domäne, Priorität, Body, Relationen). |
| `src/Core/Benchmark/RetrievalMetrics.cs` | Reine Metrik-Berechnung (Recall@k, MRR, Perzentile). |
| `src/Core/Exceptions/*.cs` | Domänen-Exceptions (z. B. `RuleParseException`). |

## Abhängigkeiten

### Intern
Keine — Core ist die Wurzel; **alle** anderen Projekte referenzieren Core.

### Extern (Packages)
Keine. Reine BCL (Records, `TimeProvider`, `System.Text.Json` in `KnowledgeBundle`).

## Öffentliche API / Interface

In Edda implementierte Verträge (Auswahl, gruppiert):

- **Wissensgraph:** `IKnowledgeGraph`, `ICypherExecutor`, `IGraphDatabaseProvider`,
  `IDomainManager`, `IEntityStore`, `IRuleLoader`, `IWorldKnowledgeSeeder`, `IGraphValidator`,
  `INeo4jEmbeddingCache`, `IBenchmarkRunner`, `IRuleFeedbackService`, `IRuleConfidenceStore`.
- **Embeddings:** `IEmbeddingService`.
- **Ingestion/Connectoren:** `IIngestionPipeline`, `IIngestionSource`, `IIngestionEnricher`,
  `IKnowledgeConnector`, `IConnectorRegistry`, `IKnowledgeImporter`, `IArchiveExtractor`,
  `IPdfTextExtractor`, `IGitClient`, `IGitLabClient`, `IGitLabClientFactory`.
- **Tools/TDK:** `IToolRegistry`, `IToolExecutor`, `IAgentTool`, `ITdkEngine`, `IToolKnowledgeService`.
- **Security/Konfiguration:** `ICredentialStore`, `IAuditLog`, `ISettingsService`, `IFileSystem`,
  `ITaintTracker`, `IIdentityContext`.
- **Sandboxing:** `ISandboxFactory`.

## Datenfluss / Call-Flow

Core enthält keine Laufzeitbeteiligung. Die Implementierungen liegen in `AKG`, `Embeddings`,
`AKG.Ingestion`, `AKG.Mcp`, `Agent`, `Security`, `Sandboxing`; verdrahtet werden sie in `Edda.Hosting`.

## Offene Fragen / TODOs

- Einige Verträge sind aus dem Monorepo geerbt, in Edda aber **ohne Implementierung** (z. B. `IModelClient`,
  `IAgentRuntime`, `IWorkflowEngine`, `ICloneOrchestrator`, Channel-/Browser-Modelle). Kandidaten zum
  Entfernen, um die Vertrags-Oberfläche auf den tatsächlichen Umfang zu reduzieren.
