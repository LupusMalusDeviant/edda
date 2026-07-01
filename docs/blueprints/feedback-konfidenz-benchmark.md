# Feedback, Konfidenz & Benchmark

## Zweck

Die Qualitäts- und Lernschicht des Wissensgraphen: ein Rule-Feedback-Loop (F32) passt die Konfidenz von
Regeln anhand von Validierungs-/Nutzungs-Ergebnissen an (SQLite-persistiert), ein Konfidenz-Store liefert
gleitende Konfidenzwerte für die TDK-Engine, und der Retrieval-Benchmark (F48) misst die Qualität von
`CompileContextAsync`. Ein zeitbasierter **Decay** (F32b) lässt veraltetes Feedback verblassen
(Vergessenskurve). Damit verbessert sich die Kontext-Auswahl über die Zeit messbar.

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/AKG/Feedback/RuleFeedbackService.cs` | `IRuleFeedbackService` — verbucht Feedback, liefert Konfidenz-Multiplikatoren. |
| `src/AKG/Feedback/RuleFeedbackStore.cs` · `IRuleFeedbackStore.cs` | SQLite-Persistenz des Feedbacks. |
| `src/AKG/Feedback/ConfidenceAdjuster.cs` | Rechnet Outcomes in Konfidenz-Anpassungen um; wendet zeitbasierten **Decay** an (altes Feedback fällt Richtung neutral zurück). |
| `src/AKG/Feedback/FeedbackSummaryJob.cs` | Hosted Service — periodische Aggregation. |
| `src/AKG/Confidence/RuleConfidenceStore.cs` | In-Memory-Konfidenz-Store (thread-safe). |
| `src/AKG/Confidence/SlidingWindowRuleConfidenceStore.cs` | `IRuleConfidenceStore` — gleitendes Fenster für die TDK-Engine. |
| `src/AKG/Benchmark/AkgBenchmarkRunner.cs` | `IBenchmarkRunner` — misst Retrieval-Qualität über ein Datenset. |

## Abhängigkeiten

### Intern
- **Core** — `IRuleFeedbackService`, `IRuleFeedbackStore`, `IRuleConfidenceStore`, `IBenchmarkRunner`, `FeedbackModels`, `BenchmarkModels`.
- **Wissensgraph (AKG)** — `IKnowledgeGraph` (Benchmark ruft `CompileContextAsync`); Feedback fließt als Multiplikator in die Kompilierung zurück.

### Extern (Packages)
- `Microsoft.Data.Sqlite` — Persistenz des Feedback-Stores (`FEEDBACK_DB_PATH`, Default `data/feedback.db`).
- Konfidenz-Decay konfigurierbar über `FEEDBACK_DECAY_HALFLIFE_DAYS` (Halbwertszeit in Tagen, Default 90, `<= 0` deaktiviert).

## Öffentliche API / Interface

- `IRuleFeedbackService` — Feedback verbuchen + Konfidenz-Multiplikator je Regel abfragen.
- `IRuleConfidenceStore` — gleitende Konfidenz für TDK.
- `IBenchmarkRunner.RunAsync(dataset, k, ct)` → Retrieval-Metriken (Recall@k, MRR …).
- REST: `POST /api/akg/benchmark` (AdminOnly) stößt einen Lauf an.

## Datenfluss / Call-Flow

1. Nutzung/Validierung erzeugt Feedback → `RuleFeedbackService` persistiert es (SQLite) und
   `ConfidenceAdjuster` aktualisiert die Konfidenz.
2. Bei der nächsten `CompileContextAsync` wirkt die Konfidenz als **F32-Multiplikator** auf das Ranking
   (siehe Feature *Wissensgraph*).
3. `FeedbackSummaryJob` rechnet täglich neu; dabei wendet der `ConfidenceAdjuster` einen **Decay** an:
   Regeln ohne frisches Feedback (`LastFeedbackAt`) fallen exponentiell (Halbwertszeit
   `FEEDBACK_DECAY_HALFLIFE_DAYS`) Richtung neutral zurück. `AkgBenchmarkRunner` misst die
   Retrieval-Qualität gegen ein Referenz-Datenset.

## Offene Fragen / TODOs

Keine offenen Punkte bekannt.
