# MCP — Anbindung an beliebige Agenten

Der MCP-Server exponiert die intern registrierten `IAgentTool`-Instanzen spec-konform über das
offizielle `ModelContextProtocol`-SDK. Ausführung läuft durch `IToolExecutor` und erbt damit
Secret-Redaction, Taint-Check, Audit-Log und Timeout der internen Tool-Pipeline.

## Transporte

| Transport | Host | Endpoint / Start |
|-----------|------|------------------|
| HTTP/SSE (streamable) | `src/Web` | `GET/POST http://127.0.0.1:8080/mcp` (opt-in: `MCP_SERVER_ENABLED=true`) |
| stdio | `src/Edda.Mcp.Stdio` | `dotnet run --project src/Edda.Mcp.Stdio` |

Beide Hosts teilen sich die Verdrahtung: `AddEddaCore` + `AddEddaMcpHandlers`.
Nur der Transport unterscheidet sich (`WithHttpTransport` vs. `WithStdioServerTransport`).

## Allow-Liste (Default-Deny)

Nur erlaubte Tools werden via `tools/list` angekündigt und sind über `tools/call` aufrufbar.
Die Default-Allowlist umfasst **genau die zwei Lese-Tools** `search_memory` und `list_memory`.
`analyze_coverage` (read-only Coverage-Report) und `tdk_validate` sind standardmäßig **nicht**
exponiert, lassen sich aber bei Bedarf über `MCP_EXPOSED_TOOLS` (Komma-getrennt) freischalten:

```
MCP_EXPOSED_TOOLS=search_memory,list_memory,analyze_coverage,tdk_validate
```

Defense-in-depth: Ein nicht-gelistetes Tool wird auch dann abgelehnt, wenn ein Client einen Call
für ein nie angekündigtes Tool baut (`McpServer.CallToolAsync`).

## Connect-Handshake (`instructions`)

Beim Verbinden sendet der Server einen `instructions`-Text, der Edda als das **persistente
Langzeitgedächtnis** des Agents rahmt und Clients anweist, zuerst `search_memory` aufzurufen,
bevor sie das Dateisystem durchsuchen.

## Auth

- **Ohne Token** (Default): loopback-Bind, jeder Request gilt als lokaler Admin (`sub=local`).
- **Mit `EDDA_AUTH_TOKEN`**: `/api/akg/*` und `/mcp` verlangen
  `Authorization: Bearer <token>` (oder `?token=<token>`), sonst 401.

Der MCP-Nutzerkontext (`ToolExecutionContext.UserId`) kommt aus dem `sub`-Claim
(HTTP) bzw. ist im stdio-Host `null` (Tools tolerieren das → `anonymous`/`local`).

## Client-Beispiele

HTTP/SSE:
```jsonc
{ "mcpServers": { "edda": { "url": "http://127.0.0.1:8080/mcp" } } }
```

stdio:
```jsonc
{ "mcpServers": { "edda": {
    "command": "dotnet",
    "args": ["run", "--project", "src/Edda.Mcp.Stdio"] } } }
```

## Externe MCP-Tools importieren (optional)

`AKG.Mcp` enthält auch einen Client (`McpToolImporter`/`ExternalMcpClient`), der externe
MCP-Server als interne Tools registrieren kann (`EXTERNAL_MCP_SERVERS`, `N8N_MCP_URL`).
Im schlanken Default ist er inaktiv.
