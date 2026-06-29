# Web-UI (Blazor-Host)

## Zweck

Der Blazor-Server-Host — zugleich Web-UI, REST-Host und MCP-über-HTTP/SSE-Host. `Program.cs` ist die
Anwendungs-Komposition (`AddEddaCore` + Auth + Razor + `MapStaticAssets` + Endpoints + optional `/mcp`).
Die Seiten geben Wissensgraph, TDK, Embeddings, Einstellungen (inkl. LLM- & MCP-Konfig),
Wissensquellen und Import frei — im Glassmorphism-Design, lokalisiert (de/en).

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/Web/Program.cs` | Komposition: Core-DI, Auth, Razor (InteractiveServer), `MapStaticAssets`, Endpoints, `/mcp`. |
| `src/Web/Components/App.razor` · `Routes.razor` | Host-Page (fingerprintete Assets) + Routing. |
| `src/Web/Components/Pages/Knowledge.razor` | Wissensgraph (Cytoscape) + Mehrfach-Auswahl/Löschen. |
| `src/Web/Components/Pages/{Tdk,Embeddings,Settings,Sources,Import}.razor` | TDK, Embeddings, Einstellungen (LLM + MCP), Quellen, Import. |
| `src/Web/Components/AKG/KnowledgeGraphView.razor` | Cytoscape-Wrapper + JS-Interop (Box-Select). |
| `src/Web/Components/AKG/{RuleEditor,RuleDetail}.razor` | Regel-Editor/-Detail. |
| `src/Web/Components/Layout/{MainLayout,NavMenu,ThemeToggle,LanguageToggle,ReconnectModal}.razor` | Layout, Navigation, Theme/Sprache. |
| `src/Web/Services/LocalizationService.cs` | `ILocalizationService` (de/en-Strings). |
| `src/Web/wwwroot/js/cytoscape-interop.js` | Graph-Render + Box-Selection-Modus. |
| `src/Web/wwwroot/js/{theme,locale,auth}.js` · `app.css` | Theme/Locale/Auth-Interop, Glassmorphism-CSS. |

## Abhängigkeiten

### Intern
- **Hosting, API & Auth** — `AddEddaCore`, Endpoints, Auth-Policies.
- Über DI direkt genutzte Core-Verträge: `IKnowledgeGraph`, `ISettingsService`, `ICredentialStore`,
  `IConnectorRegistry`, `IKnowledgeImporter`, `IToolRegistry`, `IAuditLog`, `IIdentityContext`.

### Extern (Packages)
- `ModelContextProtocol.AspNetCore` — MCP über HTTP/SSE (`MapMcp("/mcp")`).
- Selbst-gehostet in `wwwroot/lib`: `cytoscape.min.js`, Bootstrap (keine CDNs, Regel 12).

## Öffentliche API / Interface

- Seiten/Routen: `/` bzw. `/knowledge`, `/tdk`, `/embeddings`, `/settings`, `/sources`, `/import`.
- `/mcp` (HTTP/SSE) wenn `MCP_SERVER_ENABLED=true`.
- JS-Interop: `cytoscapeInterop.init/setSelectMode/getSelectedIds` (vom Wissensgraph-Reiter genutzt).

## Datenfluss / Call-Flow

1. Blazor-Server rendert interaktiv; die Seiten injizieren die Core-Interfaces direkt (keine HTTP-Hops im UI).
2. Konfigurations-Seiten schreiben über `ISettingsService.SaveAsync` / `ICredentialStore` → die
   Resolving-Fassaden greifen sofort (Live-Apply).
3. Der Wissensgraph rendert über `cytoscape-interop.js`; statische Assets sind content-gehasht
   (`MapStaticAssets` + `@Assets[...]`), daher kein veralteter Browser-Cache nach Änderungen.

## Offene Fragen / TODOs

Keine offenen Punkte bekannt.
