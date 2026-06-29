# Architektur

## Schichten

```
Core  (reine Verträge: Interfaces + Models, keine Abhängigkeiten)
 ├─ AKG          → Neo4j.Driver, Microsoft.Data.Sqlite
 ├─ Embeddings   (6 IEmbeddingService-Provider)
 ├─ Security     (SecretRedactor, HmacAuditLog, Sanitizer, Taint, Credentials)
 ├─ Sandboxing   → Docker.DotNet (ISandboxFactory: docker/wasm/null)
 ├─ AKG.Mcp      → ModelContextProtocol (Server + Client + Adapter)
 └─ Agent        → Core, Security   (TDK-Engine, ToolRegistry, 6 Tools, PhysicalFileSystem)

Edda.Hosting → alle Libs
   (AddEddaCore, AddEddaMcpHandlers, /api/akg/*-Endpoints, LocalIdentityContext, lokale Auth)
   ├─ Web                    (Blazor + REST + MCP über HTTP/SSE)   — Assembly Edda.Web
   └─ Edda.Mcp.Stdio  (MCP über stdio)
```

Der Graph ist azyklisch. `Agent` referenziert bewusst **nicht** `AKG` — die Tools sprechen
`IKnowledgeGraph`/`ITdkEngine` als **Core-Interfaces** an; die konkrete AKG-Implementierung wird
erst im Host (`AddEddaCore`) verdrahtet.

## DI-Fluss (`AddEddaCore`)

`Edda.Hosting/DependencyInjection/EddaServiceExtensions.cs` komponiert in dieser
Reihenfolge (beide Hosts nutzen dieselbe Methode):

1. `TimeProvider.System`, `IHttpContextAccessor`, `IIdentityContext` → `LocalIdentityContext`
2. `AddEmbeddingService(config)` — wählt Provider per `EMBEDDING_PROVIDER`
3. `AddSecurityServices()` — SecretRedactor, HmacAuditLog, …
4. `AddAkgServices(config)` — Graph-Provider (Neo4j/Memgraph), `ICypherExecutor`, `IKnowledgeGraph`,
   Soul, Domains, ContextCompiler, Confidence, Feedback, Benchmark, Embedding-Cache
5. `AddTdkEngine()` — `ToolRegistry` (= `IToolExecutor`+`IToolRegistry`), `TdkEngine`, `IFileSystem`
6. `AddLeanAgentTools()` — 6 `IAgentTool` + `IToolKnowledgeService` + Startup-Registrar
7. `AddMcpServices()` — `McpServer`, `McpToolRegistry`, `McpProtocolHandlers` (Importer dormant)
8. `AddSandboxingServices()` — `ISandboxFactory` (docker/wasm/null)

`IModelClient` (Chat-LLM) wird **nicht** registriert — die schlanke Variante kennt keine
LLM-basierte Wissens-Extraktion.

## AKG-Kontext-Kompilierung (4 Phasen)

`AKG/Context/ContextCompiler.cs`: (1) Keyword-Scoring → (2) semantisches Boosting
(`SemanticBooster`, Vektorindex mit App-seitigem Cosine-Fallback) → (3) MMR-Reranking →
(4) Konflikt-/Ausnahme-Auflösung. Soul-Regeln werden immer eingespeist, unabhängig vom Scoring.

## Graph-Datenbank

`GRAPH_PROVIDER` wählt `Neo4jGraphDatabaseProvider` (Default) oder `MemgraphGraphDatabaseProvider`.
Neo4j nutzt den nativen Vektorindex (`db.index.vector.queryNodes`); fehlt er (z. B. Memgraph),
fällt `SemanticBooster` automatisch auf App-seitige Cosine-Ähnlichkeit zurück — beide Provider
funktionieren.
