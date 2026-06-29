# AKG.Mcp — Model Context Protocol

Bidirektionale MCP-Integration: Der Agent kann als MCP-Server nach außen exponiert werden (interne Tools → externe Clients) und externe MCP-Server als Tools importieren (externe Server → interne Tool-Registry).

---

## Abhängigkeiten

```
AKG.Mcp → Core, AKG
```

---

## Verzeichnisstruktur

```
AKG.Mcp/
├── Adapter/          ← McpAdapter (intern → extern)
├── Client/           ← MCP-Client (extern → intern)
├── DependencyInjection/
├── Models/           ← MCP-DTOs
└── Server/           ← MCP-Server-Implementierung
```

---

## Server/ — Agent als MCP-Server

Exponiert die internen Tools nach außen über das offizielle **`ModelContextProtocol`-SDK**
(spec-konformes Streamable-HTTP-JSON-RPC). Das Hosting (`AddMcpServer().WithHttpTransport()`
+ `app.MapMcp("/mcp")`) liegt im Gateway (Composition Root), opt-in via `MCP_SERVER_ENABLED`
mit `RequireAuthorization`.

### `McpServer.cs`
Geguardeter Dispatch: führt `tools/call` über `IToolExecutor` aus (erbt Secret-Redaction,
Taint-Check, Audit) und lehnt nicht-allowgelistete Tools ab.

### `McpExposurePolicy.cs`
Default-Deny-Allow-List, welche internen Tools nach außen sichtbar/aufrufbar sind
(konfigurierbar via `MCP_EXPOSED_TOOLS`; Default: die zwei read-only Memory-Tools
`search_memory` und `list_memory`).

### `McpProtocolHandlers.cs`
Bridge zwischen interner Tool-Schicht und den SDK-Handlern: bedient `tools/list` (Allow-List-
gefiltert) und `tools/call` (über den geguardeten `McpServer`).

### `McpToolRegistry.cs`
Liefert die allow-gelisteten internen Tools als MCP-Tool-Definitionen.

---

## Client/ — Externe MCP-Server importieren

Importiert Tools von externen MCP-Servern in die interne Tool-Registry.

### `ExternalMcpClient.cs` / `IExternalMcpClient.cs`
HTTP-Client für externe MCP-Server. Ruft `tools/list` ab und führt `tools/call` aus.

### `McpToolImporter.cs`
Importiert alle Tools eines externen MCP-Servers und registriert sie als `IAgentTool`-Wrapper im `ToolRegistry`.

### `McpToolSource.cs`
Konfiguration einer externen MCP-Quelle (URL, API-Key, aktiviert/deaktiviert).

---

## Adapter/

### `McpAdapter.cs`
Konvertiert zwischen internen `ToolDefinition`/`ToolResult`-Typen und MCP-Protokoll-Typen. Übernimmt JSON-Schema-Konvertierung.

---

## Models/

| Datei | Typ | Beschreibung |
|---|---|---|
| `McpToolDefinition.cs` | Record | MCP-Tool-Schema (name, description, inputSchema) |
| `McpToolCall.cs` | Record | Eingehender Tool-Aufruf (id, name, arguments) |
| `McpToolResult.cs` | Record | Tool-Ergebnis (id, content, isError) |
| `McpTextContent.cs` | Record | Text-Inhalt einer MCP-Antwort |

---

## Umgebungsvariablen

| Variable | Beschreibung |
|---|---|
| `MCP_SERVER_ENABLED` | `true` aktiviert den MCP-Server-Endpunkt (`/mcp`, spec-konform via SDK) |
| `MCP_EXPOSED_TOOLS` | Allow-List der nach außen exponierten Tools (Default-Deny). Leer = sichere Defaults (`search_memory`, `list_memory`) |
| `MCP_EXTERNAL_SERVERS` | Kommaseparierte Liste externer MCP-Server-URLs |
