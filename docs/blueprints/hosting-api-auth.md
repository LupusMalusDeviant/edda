# Hosting, API & Auth

## Zweck

Die Kompositions-Wurzel und HTTP-Oberfläche von Edda: `AddEddaCore` verdrahtet alle Bibliotheks-Projekte
per DI, die `Map…Endpoints`-Erweiterungen registrieren die REST-API, und die lokale Authentifizierung
stellt eine loopback-freundliche Single-Admin-Identität (optional Bearer-Token) samt der Policies
`default` und `AdminOnly` bereit, die die AKG-Endpoints und `/mcp` verlangen. Wird von beiden Hosts genutzt
(Web + stdio).

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/Edda.Hosting/DependencyInjection/EddaServiceExtensions.cs` | `AddEddaCore` (alle Libs), `AddEddaLocalAuth`, `AddEddaMcpHandlers`. |
| `src/Edda.Hosting/Api/AkgEndpoints.cs` · `AkgEndpointHandlers.cs` | `/api/akg/*` (Regeln, Stats, Kontext, Reload, Propose, Embed-Rebuild, Benchmark, Ingest). |
| `src/Edda.Hosting/Api/SettingsEndpoints.cs` | `/api/settings`, `/api/credentials`, `/api/settings/llm/test`. |
| `src/Edda.Hosting/Api/ConnectorEndpoints.cs` | `/api/connectors`, `/api/sources` (CRUD) + `/run`. |
| `src/Edda.Hosting/Api/KnowledgeExportEndpoints.cs` | `/api/knowledge/export`. |
| `src/Edda.Hosting/Api/IngestionEndpointHandlers.cs` | `/api/akg/ingest` (credential-frei). |
| `src/Edda.Hosting/Authentication/LocalAuthenticationHandler.cs` | Loopback-Admin + optionaler `EDDA_AUTH_TOKEN`-Bearer. |
| `src/Edda.Hosting/Identity/LocalIdentityContext.cs` | `IIdentityContext` (UserId, IsAdmin). |

## Abhängigkeiten

### Intern
- **Alle** Bibliotheks-Features: Core, Wissensgraph (AKG), Feedback/Benchmark, Embeddings, Ingestion &
  Connectoren, Wissens-Import, MCP, Agent-Tools & TDK, Sandboxing, Security & Konfiguration.

### Extern (Packages)
- ASP.NET Core (`FrameworkReference Microsoft.AspNetCore.App`) — Minimal-API-Endpoints + Auth.

## Öffentliche API / Interface

REST (Auswahl; `auth` = authentifiziert, `AdminOnly` = Admin-Policy):

| Methode | Route | Auth |
|---|---|---|
| GET | `/api/akg/rules` · `/rules/{id}` · `/stats` · `/context` | auth |
| POST/DELETE | `/api/akg/propose` · `/api/akg/rules/{id}` | auth / AdminOnly |
| POST | `/api/akg/reload` · `/embed/rebuild` · `/benchmark` · `/ingest` | AdminOnly |
| GET/PUT | `/api/settings` | auth / AdminOnly |
| GET/PUT/DELETE | `/api/credentials` · `/api/credentials/{name}` | AdminOnly |
| GET | `/api/connectors` · `/api/sources` | auth |
| POST/PUT/DELETE | `/api/sources` · `/api/sources/{id}` · `/api/sources/{id}/run` | AdminOnly |
| GET | `/api/knowledge/export` | auth |
| GET | `/health` | anonym |

`AddEddaCore(IConfiguration)` ist die gemeinsame Komposition; `AddEddaLocalAuth()` liefert die Policies.

## Datenfluss / Call-Flow

1. Der Host (`Web` bzw. `Edda.Mcp.Stdio`) ruft `builder.Services.AddEddaCore(...)` → registriert alle
   Features inkl. Resolving-Fassaden + Hosted Services (World-Seed, Feedback, Tool-Registrierung).
2. `AddEddaLocalAuth()` setzt Authentifizierung + `AdminOnly`-Policy; Loopback gilt als Admin, sonst greift
   der `EDDA_AUTH_TOKEN`-Bearer.
3. Die `Map…Endpoints`-Aufrufe in `Program.cs` hängen die REST-Routen ein; Secrets werden nie aus
   Request-Bodies übernommen (Connectoren liefern sie server-seitig).

## Offene Fragen / TODOs

Keine offenen Punkte bekannt.
