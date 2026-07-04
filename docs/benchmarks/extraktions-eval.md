# Extraktions-Eval: LLM-Entitäten/Relationen

Ein Harness, das die Qualität der LLM-Extraktion (`IEntityExtractor` → Entitäten +
Relationen aus Rohtext) gegen ein kuratiertes Golden-Set misst — Precision /
Recall / F1.

## ⚠️ Status: gebaut, aber NICHT gegen ein echtes LLM verifiziert

Harness, Scoring und Golden-Set sind implementiert und in den Unit-Tests gegen
einen **gemockten** `ILlmChatClient` grün. Die **echte Extraktionsqualität** (mit
einem lokalen Ollama-Modell) wurde hier **nicht** gemessen — in dieser Umgebung
läuft kein LLM. Das ist ein offener, **nutzerseitiger** Schritt (siehe unten).
Es gibt hier also bewusst **keine** Qualitätszahl; belegt ist nur, dass der
Extraktions-/Parse-/Scoring-Pfad end-to-end funktioniert.

## Aufbau

- `IExtractionEvaluator` (Core) / `ExtractionEvaluator` (AKG.Ingestion): läuft jede
  Case durch den Extractor und scored den Output gegen das Golden.
  - **Entitäten**: Match per normalisiertem Namen (trim + lowercase).
  - **Relationen**: Match per normalisiertem `(Source, Target)`-Paar.
  - Beschreibungen/Keywords sind advisory und gehen nicht ins Scoring. Matching
    ist set-basiert (Duplikate blähen nicht auf); Aggregat ist der Macro-Average
    über die Cases.
- `CuratedExtractionEvalSet.Default`: kleines, handkuratiertes Golden-Set (aktuell
  3 Cases: Neo4j/Cypher, Acme/Berlin, Photosynthese) — bewusst klein, erweiterbar.

## So misst du gegen echtes Ollama (nutzerseitig)

1. Ollama lokal starten und ein Modell ziehen (z. B. `ollama pull qwen2.5:7b`).
2. Extraktion aktivieren: `INGESTION_ENTITY_EXTRACTION=true`,
   `INGESTION_LLM_PROVIDER=ollama` (ADR-0010).
3. Den realen `LlmEntityExtractor` (über den Ollama-`ILlmChatClient`) an den
   Evaluator geben:

```csharp
var extractor = /* LlmEntityExtractor über den Ollama-ILlmChatClient */;
var report = await new ExtractionEvaluator()
    .EvaluateAsync(extractor, CuratedExtractionEvalSet.Default);
// report.EntityScore.F1 / report.RelationScore.F1 auswerten …
```

4. Für belastbare Zahlen das Golden-Set vergrößern und mehrere Modelle vergleichen.
   Kleine lokale Modelle liefern erwartungsgemäß rauschendere Graphen (analog
   Cognees ~32B-Caveat).

## Was die Unit-Tests belegen (mit gemocktem LLM)

- Scoring korrekt: Precision/Recall/F1, case-insensitiv + getrimmt, Teiltreffer,
  leere Mengen.
- Der **echte** `LlmEntityExtractor` + ein gemockter `ILlmChatClient` (kanned JSON)
  laufen durch den Evaluator → F1 = 1,0 auf dem passenden Golden. Damit ist der
  Extraktions-/Parse-/Scoring-Pfad deterministisch abgesichert — es fehlt nur das
  reale Modellverhalten.
