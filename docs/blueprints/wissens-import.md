# Wissens-Import (Upload & Bundle)

## Zweck

Importiert hochgeladenes Wissen über das UI: Markdown (Einzeldatei / ZIP-Sammlung), PDF, HTML
(Confluence/Notion), Datensatz-Formate (CSV, JSONL, JSON-Array — auch Vektor-DB-Dumps) sowie das
verlustfreie JSON-**Wissensbündel** (Im- und Export). Uploads werden **strukturiert** abgebildet — wie eine
Git-Ingestion: Wurzel → Quelle → Dateien mit Pfad-Domänen und aufgelösten Querverweisen, statt flacher
Einzelknoten. Embeddings werden lokal neu berechnet; **Fremd-Vektoren werden nie übernommen** (ADR-0007).

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/AKG.Ingestion/Import/KnowledgeImporter.cs` | `IKnowledgeImporter` — dispatcht nach Format; nutzt `GitMarkdownSource`-Builder + `IngestionItemMapper` für eine verbundene Hierarchie; JSON-Bündel-Import. |
| `src/AKG/Import/ZipArchiveExtractor.cs` | `IArchiveExtractor` — In-Memory-ZIP (zip-slip-immun). |
| `src/AKG/Import/PdfPigTextExtractor.cs` | `IPdfTextExtractor` — PDF-Text via PdfPig. |
| `src/Core/Models/KnowledgeBundle.cs` | Kanonisches Im-/Export-Format (`Rules` + Relationen) + Serializer-Optionen. |
| `src/Edda.Hosting/Api/KnowledgeExportEndpoints.cs` | `GET /api/knowledge/export` → Bündel als JSON-Download. |
| `src/Web/Components/Pages/Import.razor` | Upload-UI (Formatauswahl, Bezeichnung der Quelle). |

## Abhängigkeiten

### Intern
- **Core** — `IKnowledgeImporter`, `IArchiveExtractor`, `IPdfTextExtractor`, `KnowledgeBundle`, `KnowledgeRule`.
- **Ingestion & Connectoren** — `IngestionItemMapper` + `GitMarkdownSource`-Builder (gemeinsame Mapping-Logik).
- **Wissensgraph (AKG)** — `IKnowledgeGraph.UpsertRuleAsync`; Extraktoren liegen im AKG-Projekt.

### Extern (Packages)
- `UglyToad.PdfPig` — PDF-Textextraktion.
- BCL `System.IO.Compression` — ZIP (in-memory).

## Öffentliche API / Interface

- `IKnowledgeImporter.ImportAsync(fileName, byte[] content, string? domain, ct)` → `IngestionResult`.
- Formate: `.md`/`.markdown`, `.zip` (md + html), `.pdf`, `.html`/`.htm`, `.json` (Bündel **oder** Array),
  `.jsonl`, `.csv`.
- `GET /api/knowledge/export` (auth) → `KnowledgeBundle` (Dateiname `edda-knowledge-bundle.json`).

## Datenfluss / Call-Flow

1. `ImportAsync` dispatcht nach Endung:
   - **`.json`** → Bündel (Regeln verlustfrei upserten) **oder** Array (Datensätze → Items).
   - **`.csv` / `.jsonl`** → Datensätze → Items (Felder `id`/`title`/`body`/`content`/`text`/`document`…).
   - **`.md` / `.zip` / `.pdf` / `.html`** → Text-Einträge (HTML wird tag-bereinigt).
2. Aus den Einträgen baut `IngestItemsAsync` eine verbundene Hierarchie (`git-knowledge` → `git:<quelle>` →
   Dateien) via `GitMarkdownSource`-Builder.
3. `IngestionItemMapper` mappt jedes Item → `KnowledgeRule` (Pfad-Domänen, aufgelöste Relationen) →
   `UpsertRuleAsync`.
4. Anschließend ggf. Re-Embedding (Feature *Embeddings*), damit die Inhalte semantisch durchsuchbar sind.

## Offene Fragen / TODOs

- Weitere Fremdformate (z. B. Confluence-Space-XML) sind über zusätzliche Adapter denkbar; aktuell deckt der
  HTML-Tag-Strip die gängigen Exporte ab.
