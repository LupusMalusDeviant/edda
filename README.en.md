# Edda

[Deutsch](README.md) · **English**

Standalone **Agent Knowledge Graph (AKG)** + **Test-Driven Knowledge (TDK)** with embeddings,
exposed as an **MCP server** (HTTP/SSE and stdio) with its own **Blazor UI**. Local-only —
any agent/LLM can attach to the knowledge graph over MCP (**read-only**).

Stack: .NET 10 · C# 13 · Neo4j 5 (or Memgraph) · Blazor Server · ModelContextProtocol 1.4

> **New here?** The **[Getting-Started guide](docs/erste-schritte.md)** walks you through
> installation → creating your first rule → attaching an agent → TDK validation in 15 minutes.
> Unfamiliar terms are explained in the **[glossary](docs/glossar.md)**.
> _(The `docs/` are currently written in German.)_

---

## Who it's for & why

Edda is a **local long-term memory for coding agents** — curated, cross-project knowledge that any
MCP-capable agent (Claude Code, Cursor, …) can tap read-only. Three typical uses:

- **Private dev store** — personal coding conventions, architecture decisions and "lessons learned",
  local and cloud-free.
- **Team knowledge base** — shared standards and domain knowledge that agents apply consistently.
- **Coding-standards guardian** — with **TDK**, rules actively check generated code, not just advise.

Deliberately different from generic memory frameworks: **safety-first MCP** (read-only, default-deny —
safe to expose to third-party agents), **no large LLM required** (curated knowledge instead of LLM
extraction, runs on weak hardware) and **.NET-native**.

---

## Requirements

- **Docker** + **Docker Compose v2** (to run it)
- Optional, for container-free development: **.NET 10 SDK**

## Installation (recommended)

Clone the repo and run the install script for your platform. It asks for **target host/port** and
**optional first keys** (embedding/LLM — skippable with Enter), writes `.env`, builds + starts the
containers, **tests reachability** and prints the clickable link at the end.

```bash
git clone https://github.com/LupusMalusDeviant/edda.git
cd edda
```

**Mac / Linux:**
```bash
./install.sh
```

**Windows (PowerShell):**
```powershell
.\install.ps1
# If execution is blocked:
#   powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Afterwards Edda runs at the link shown (default `http://127.0.0.1:8080`).
Stop: `docker compose down` · Logs: `docker compose logs -f edda`.

> **Remote access:** If a host other than `127.0.0.1`/`localhost` is given, Edda binds on all
> interfaces (`0.0.0.0`). In that case be sure to set an **auth token** (the script asks for it).

### Manual (without the script)
```bash
cp .env.example .env       # adjust values (bind/port, keys, MCP, token)
docker compose up -d --build
```

### Development (without containers)
```bash
docker compose up -d neo4j
dotnet run --project src/Web      # → http://127.0.0.1:8080
```

---

## Connecting MCP to LLMs (read-only)

Edda exposes the knowledge graph as an MCP server. **Attached LLMs get read access only:**

- Default allowlist (**default-deny**): `search_memory`, `list_memory` — both read-only.
  `tdk_validate` is **not** exposed by default but can be enabled via `MCP_EXPOSED_TOOLS`.
- **Write tools** (`manage_memory`, `manage_userdata`, `manage_learnings`) are **blocked over MCP as a
  rule** — even if accidentally listed in `MCP_EXPOSED_TOOLS`. Only an explicit
  `MCP_ALLOW_WRITE_TOOLS=true` lifts that (for trusted setups).
- **Configurable in the web UI:** under *Settings → MCP server* you can toggle exposure (on/off), the
  exposed tools and write access **live** (no restart). UI values take precedence over the `MCP_*`
  environment variables.
- **Connect handshake:** on connect the server sends an `instructions` text describing Edda as the
  agent's persistent long-term memory and instructing clients to call `search_memory` first.

**HTTP/SSE** (Claude Code, Cursor, remote clients) — `.mcp.json`:
```jsonc
{
  "mcpServers": {
    "edda": {
      "url": "http://<host>:8080/mcp",
      "headers": { "Authorization": "Bearer <EDDA_AUTH_TOKEN>" }
    }
  }
}
```
> Without a set `EDDA_AUTH_TOKEN`, `/mcp` is reachable only over loopback (no header needed).

**stdio** (local clients, e.g. Claude Desktop):
```jsonc
{
  "mcpServers": {
    "edda": { "command": "dotnet", "args": ["run", "--project", "src/Edda.Mcp.Stdio"] }
  }
}
```

---

## Configuration (`.env`)

| Variable | Purpose |
|---|---|
| `EDDA_BIND` / `EDDA_PORT` | Host bind (`127.0.0.1` = local, `0.0.0.0` = remote) + port |
| `EMBEDDING_PROVIDER` / `EMBEDDING_API_KEY` | Embeddings (`openai`/`google`/`voyage`/`ollama`/`custom`/`null`) |
| `INGESTION_ENRICHER=llm` + `INGESTION_LLM_PROVIDER` / `_API_KEY` | LLM enrichment during import |
| `EDDA_AUTH_TOKEN` | Bearer token for `/api/*` and `/mcp` (empty = loopback only) |
| `MCP_SERVER_ENABLED` | MCP server on/off |
| `MCP_EXPOSED_TOOLS` | Allowlist of MCP tools (default: the 2 read tools `search_memory`, `list_memory`) |
| `MCP_ALLOW_WRITE_TOOLS` | Allow write tools over MCP (default: off → read-only) |

Full template with all variables: **`.env.example`**.

---

## Documentation

_The documentation is currently written in German._

| File | Contents |
|-------|--------|
| `docs/erste-schritte.md` | **Start here:** guided 15-minute tutorial |
| `docs/glossar.md` | All domain terms explained |
| `CLAUDE.md` | Architecture, project list, rules |
| `docs/architektur.md` | Project graph + DI flow |
| `docs/mcp.md` | MCP HTTP + stdio, allowlist, auth |
| `docs/embeddings.md` | Embedding providers + rebuild |
| `docs/tdk.md` | TDK validation + sandbox |
| `docs/betrieb.md` | Compose, env variables, ports |
| `docs/adr/` | Architecture Decision Records |

## Tests

```bash
dotnet test Edda.slnx     # unit tests, no infrastructure
```

---

## Contributing

Contributions are welcome! Please read **[CONTRIBUTING.md](CONTRIBUTING.md)** (setup, build/test,
project rules) and the **[Code of Conduct](CODE_OF_CONDUCT.md)** first. Report security issues
privately via **[SECURITY.md](SECURITY.md)** — not through public issues.

## License

Edda is licensed under the **[Apache-2.0 license](LICENSE)**. © 2026 LupusMalus.
