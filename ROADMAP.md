# Edda — Roadmap: Aufschließen zu Cognee

> Strategische Roadmap für Edda (lokal-only Wissensgraph + TDK über MCP). Ziel ist
> nicht, die Breite eines finanzierten Teams (Cognee) nachzubauen, sondern die
> entscheidenden Lücken zu schließen und Eddas verteidigbare Stärken auszubauen.

## Ausgangslage

Im Vergleich mit dem finanzierten OSS-Memory-Framework Cognee (GraphRAG) liegt Edda
heute in vier Punkten zurück: automatischer Wissensgraph-Aufbau aus Rohdaten,
episodisches Agent-Gedächtnis, Mandantenfähigkeit und Ökosystem-Breite. Dafür hat
Edda echte Alleinstellungsmerkmale: **Safety-First-MCP** (read-only, default-deny),
**TDK** (Wissen validiert Code aktiv), ein **Security-/Compliance-Layer**
(HMAC/Merkle-Audit, Redaction, Taint, AES-GCM), **.NET-nativ** und **kein großes LLM
nötig** (kuratiertes Wissen, robust auf schwacher Hardware).

## Leitprinzip

3–4 Kern-Lücken schließen, die einzigartigen Trümpfe laut ausspielen, die unrealistischen
Felder bewusst auslassen. Zielkorridor: Gesamtscore von ~40 auf ~47–48/60; Kategoriesiege
im privaten/lokalen .NET-Umfeld und beim sicherheitskritischen Agent-Zugriff.

---

## Track 0 — Quick Wins: Fundament & Onboarding  *(Meilenstein M1)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 0.1 | **Zero-Infra-Dev-Modus** (eingebetteter Graph+Vektor, kein Docker) | hoch | M | `AddAkgServices`, app-seitiges Cosine-Fallback ausbauen | `dotnet run` ohne Neo4j startbar; Tests ohne Infra grün |
| 0.2 | **Confidence-Decay / Vergessenskurve** | mittel | S | `ConfidenceAdjuster`, `RuleFeedbackService` | ungenutzte Regeln verlieren Konfidenz; „stale → prüfen"-Liste abrufbar |
| 0.3 | **Proaktive Gap-Analyse** (read-only MCP-Tool `analyze_coverage`) | mittel-hoch | M | `AKG.Mcp` Tool-Registry | meldet dünne Domains, Konzepte ohne Regel, Low-Confidence, offene Konflikte; default-deny bleibt |
| 0.4 | **Entwickler-Tagebuch + Produktnarrativ** | niedrig | S | `DEVELOPER.md`, `README.md` | „für wen / wofür" steht; laufendes Tagebuch existiert |

## Track 1 — Auto-Wissensgraph aus Rohdaten  *(größter Hebel, M2)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 1.1 | **Optionale LLM-Extraktion** von Entitäten/Relationen beim Ingest | hoch | L | `LlmIngestionEnricher` (in voller Pipeline vorhanden) verdrahten | aus rohem Text/Doku entstehen typisierte Knoten+Kanten; abschaltbar; keine erfundenen Knoten |
| 1.2 | **Quell-Connectoren** pragmatisch (Dateisystem/Git/Markdown/PDF zuerst) | mittel | M | `AKG.Ingestion` / `IIngestionSource` | ≥3 Connectoren produktiv |
| 1.3 | **Entity-Layer-Retrieval** ausbauen (LightRAG-Stil ist angelegt) | mittel | M | `ContextCompiler.BuildEntityContextAsync` | Entitäts-Nachbarschaft fließt in den Kontext |

## Track 2 — Episodisches Agent-Gedächtnis  *(M2 → M3)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 2.1 | **Sitzungs-Erfassung + Konsolidierung** ins Langzeit-Graph | hoch | L | Agent-Layer, `AddTdkEngine`/Tool-Layer | Konversations-Fakten landen kuratiert im Graph |
| 2.2 | **`remember` / `recall` / `forget`** als abgesicherte Tools | hoch | M | Agent `ToolRegistry`, MCP-Exposure | persistierbar/abrufbar/vergessbar; über MCP weiterhin default-deny |
| 2.3 | **Vergessens-Policy** (Decay aus 0.2 integrieren) | mittel | S | `RuleFeedbackService` | Decay steuert Konsolidierung/Forget |

## Track 3 — Mandantenfähigkeit  *(M3)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 3.1 | **Tenants/Organisationen + Rollen** (Owner/Editor/Viewer) | hoch | L | `IIdentityContext`, Hosting-Auth | Org-Ebene zusätzlich zum User-Scoping |
| 3.2 | **Dataset-/Domain-Permissions** (read/write/share) | mittel | M | Graph-Provider, Auth | vollständige Daten-Isolation pro Tenant; getestet |

## Track 4 — Backend-Flexibilität  *(M2)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 4.1 | **Eingebetteter Graph** (Kuzu-.NET oder SQLite-Graph) als Default-Option | hoch | M | Graph-Provider-Abstraktion (deckt zugleich 0.1) | Neo4j wird optional |
| 4.2 | **Vektor-Store entkoppeln** (Provider-Interface) | mittel | M | Embeddings/AKG | Backend per Config wechselbar |

## Track 5 — Moat ausbauen: Differenzierung  *(laufend)*

| # | Vorhaben | Hebel | Aufwand | Andockpunkt | Definition of Done |
|---|----------|-------|---------|-------------|--------------------|
| 5.1 | **Safety-First-MCP** schärfen & dokumentieren | hoch | S | `AKG.Mcp` `McpExposurePolicy` | „fremden Agenten gefahrlos exponierbar" belegt |
| 5.2 | **TDK produktisieren** (Validator-Bibliothek für gängige Standards) | hoch | M | `Agent/Tdk`, `Sandboxing` | mitgelieferte Validatoren; als Coding-Standards-Wächter positioniert |
| 5.3 | **Compliance-Paket** sichtbar machen (Audit/Redaction/Taint) | mittel | S | `Security` | Zielgruppe reguliert dokumentiert |

---

## Sequenzierung (illustrative Score-Wirkung, von 60)

- **M1** (Track 0): 36 → ~40,5 — gewinnt Privat · Code.
- **M2** (Track 1 + 4 + Start Track 2): ~40,5 → ~44 — Agent-Kategorien steigen.
- **M3** (Track 2 fertig + Track 3): ~44 → ~47–48 — kippt den Gesamtvergleich gegenüber
  Cognee plausibel; SurrealDB bleibt nur bei Firma · Projektmanagement vorn.

## Bewusst NICHT verfolgt (unrealistisch solo / kein Hebel)

- 30+ Connectoren, 16+ Search-Types und Framework-Integrationen in voller Breite.
- Eigenes Cloud-/SaaS-Angebot; community-/funding-getriebene Reichweite.
- Projektmanagement-Produktfunktionen — Firma · PM bleibt SurrealDB-Domäne.

## Erfolgskriterien

- `dotnet run` ohne Docker.
- Auto-Wissensgraph aus mindestens drei Quellen, abschaltbar.
- `remember`/`recall`/`forget` produktiv, MCP weiterhin read-only per Default.
- Mandanten-Isolation getestet.
- Alleinstellungsmerkmale dokumentiert und demonstrierbar.
