# MCP-Distribution: Edda an Agenten ausliefern

Praxis-Leitfaden, wie du Edda als MCP-Server für beliebige Agenten bereitstellst.
Die Transport- und Protokoll-Details stehen in [mcp.md](mcp.md) — hier geht es um
**Ausliefern, Zero-Infra-Start, Sicherheits-Empfehlungen und Verifikation**.

## 1. Zero-Infra-Quickstart (ohne Neo4j/Docker)

Edda läuft ohne externe Datenbank im In-Memory-Modus — ideal, damit ein Agent Edda
sofort nutzen kann:

```bash
GRAPH_PROVIDER=memory EMBEDDING_PROVIDER=null \
  dotnet run --project src/Edda.Mcp.Stdio
```

- `GRAPH_PROVIDER=memory` → eingebetteter Graph (kein Neo4j).
- `EMBEDDING_PROVIDER=null` → keine Embeddings nötig (Keyword + Graph-Retrieval).
- stdout trägt den JSON-RPC-Stream, stderr die Logs — der Host trennt das bewusst.

Für dauerhaften Speicher später `GRAPH_PROVIDER=neo4j` + `docker compose up -d neo4j`.
Zum Skalierungsverhalten des In-Memory-Modus siehe
[benchmarks/akg-skalierung.md](benchmarks/akg-skalierung.md).

## 2. An Claude Desktop anbinden (stdio)

**Dev** (direkt aus dem Repo, SDK erforderlich):

```jsonc
{ "mcpServers": { "edda": {
    "command": "dotnet",
    "args": ["run", "--project", "/abs/pfad/edda/src/Edda.Mcp.Stdio"],
    "env": { "GRAPH_PROVIDER": "memory", "EMBEDDING_PROVIDER": "null" } } } }
```

**Distribution** (veröffentlichtes Binary — schneller Start, kein SDK beim Nutzer):

```bash
dotnet publish src/Edda.Mcp.Stdio -c Release -o dist/edda-mcp
```

```jsonc
{ "mcpServers": { "edda": {
    "command": "/abs/pfad/dist/edda-mcp/Edda.Mcp.Stdio",
    "env": { "GRAPH_PROVIDER": "memory", "EMBEDDING_PROVIDER": "null" } } } }
```

(Unter Windows heißt das Binary `Edda.Mcp.Stdio.exe`.) Immer **absolute Pfade** —
MCP-Clients starten den Host mit unbestimmtem Arbeitsverzeichnis.

## 3. HTTP/SSE-Clients

Für Clients, die HTTP sprechen (statt einen Prozess zu spawnen): den Web-Host mit
`MCP_SERVER_ENABLED=true` starten, dann `http://127.0.0.1:8080/mcp` anbinden. Auth
und Header-Details siehe [mcp.md](mcp.md).

## 4. Sicherheits-Empfehlungen für die Distribution

Edda ist **default-deny + read-only** ausgelegt — genau richtig, um einem fremden
Agenten gefahrlos exponiert zu werden:

| Einstellung | Default | Empfehlung |
|-------------|---------|------------|
| Exponierte Tools | `search_memory`, `list_memory` (nur Lesen) | Für nicht vertrauenswürdige Agenten so belassen |
| `MCP_EXPOSED_TOOLS` | leer → Default | Nur bei Bedarf erweitern (z. B. `analyze_coverage`) |
| `MCP_ALLOW_WRITE_TOOLS` | `false` | Nur in vertrauenswürdigen Setups auf `true` |
| `EDDA_AUTH_TOKEN` (HTTP) | leer → loopback-Admin | Bei nicht-lokaler Bindung IMMER setzen |

Schreib-Tools (`remember`, `forget`, `consolidate_memory`, `manage_*`, `rate_memory`)
bleiben selbst dann blockiert, wenn sie in `MCP_EXPOSED_TOOLS` stehen — außer
`MCP_ALLOW_WRITE_TOOLS=true`. Defense-in-depth: ein nicht-gelistetes Tool wird auch
bei einem direkten `tools/call` abgelehnt.

## 5. Verifikation

### Automatisiert (im Repo, ohne Infrastruktur)

Die Tool-Listing- und Exposure-Ebene ist unit-getestet:

- `McpExposurePolicyTests` / `McpExposurePolicyReadOnlyTests` — Default-Deny +
  Read-Only-Garantie (Schreib-Tools bleiben blockiert).
- `McpToolRegistryTests` / `McpProtocolHandlersTests` — `tools/list` und `tools/call`.
- Der In-Memory-Host bootet infrastrukturlos (`HostingTestFactory`,
  `GRAPH_PROVIDER=memory`, `EMBEDDING_PROVIDER=null`).

Ein **Prozess-Level-e2e** (den stdio-Host als Kindprozess starten und JSON-RPC über
stdin/stdout sprechen) braucht einen echten Prozess-Start und ist daher bewusst
**kein** Teil der schnellen Unit-Suite — stattdessen der manuelle Smoke-Check:

### Manueller Smoke-Check (stdio)

1. Host starten: `GRAPH_PROVIDER=memory dotnet run --project src/Edda.Mcp.Stdio`
   → keine Fehler auf stderr, der Prozess bleibt offen.
2. In Claude Desktop den `edda`-Server eintragen (siehe oben) und neu starten.
3. Tool-Liste prüfen: **nur** `search_memory` und `list_memory` sichtbar.
4. `search_memory` mit einer Testanfrage aufrufen → Antwort kommt (ein leeres
   Ergebnis ist bei frischem In-Memory-Graph in Ordnung).
5. (Negativprobe) sicherstellen, dass **kein** Schreib-Tool wie `remember` auftaucht.
