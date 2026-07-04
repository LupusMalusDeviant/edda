# Skalierungs-Benchmark: AKG-Kontext-Kompilierung

Reproduzierbarer Latenz-/Qualitäts-Benchmark von `CompileContextAsync` über den
**In-Memory-Dev-Executor** (kein Neo4j, keine Embeddings) mit deterministisch
generierten synthetischen Regeln.

## Aufbau

- **Generator:** `SyntheticBenchmarkGenerator` (`Edda.Core.Benchmark`) erzeugt aus
  `(ruleCount, caseCount, seed)` deterministisch N Regeln + Cases. Jede Regel trägt
  ein „Topic"-Token; jede Case fragt genau ein Topic ab, die erwarteten Regeln sind
  die Träger dieses Tokens (Ground-Truth by construction). Kein `Random`, keine Uhr,
  keine Infrastruktur → identische Eingaben liefern identische Ergebnisse.
- **Harness:** `ScaleBenchmarkTests` (`Edda.AKG.Tests`) baut den echten
  `InMemoryCypherExecutor`-Stack, befüllt N Regeln (Bulk-Ingest) und lässt den
  bestehenden `AkgBenchmarkRunner` (F48) laufen. N/Cases/Seed werden über
  `EDDA_BENCH_RULES` / `EDDA_BENCH_CASES` / `EDDA_BENCH_SEED` gesteuert (Default
  300/20/1, damit die Test-Suite schnell bleibt).

Wiederholen (Scale-Lauf):

```bash
EDDA_BENCH_RULES=100000 dotnet test tests/AKG.Tests \
  --filter ScaleBenchmark -l "console;verbosity=detailed"
```

## Messung (2026-07-04 · In-Memory · keyword+graph · 20 Cases · Seed 1)

| Regeln (N) | recall@10 | Latenz P50 | Latenz P95 | Ø Tokens/Query |
|-----------:|----------:|-----------:|-----------:|---------------:|
| 300        | 1,000     | 0,9 ms     | 6,1 ms     | 541            |
| 10 000     | 1,000     | 26,5 ms    | 51,1 ms    | 557            |
| 100 000    | 1,000     | 521,7 ms   | 576,0 ms   | 564            |

**Kernaussage:** Der In-Memory-Dev-Modus verarbeitet **100k Regeln** mit
**sub-sekündlicher** Kompilierungslatenz (P50 ≈ 0,5 s) — es braucht kein
Neo4j/Docker, um bei dieser Größenordnung zu entwickeln und zu testen. Die Latenz
skaliert ~linear bis leicht superlinear mit N (der In-Memory-Executor macht pro
Query Full-Scans + Scoring/MMR); der Token-Footprint pro Query bleibt konstant
(~550, das k-limitierte Kontextfenster).

## Ehrliche Grenzen (nicht überinterpretieren)

- **In-Memory ≠ Neo4j.** Gemessen wird der Dev-/Test-Pfad (`InMemoryCypherExecutor`,
  O(N)-Scans). Ein echtes Neo4j mit Indizes verhält sich bei großem N anders
  (indizierte Lookups statt Full-Scan) — die Zahlen sind **keine**
  Neo4j-Produktionslatenz.
- **Semantik aus.** Ohne Embedding-Provider läuft nur Keyword + Graph (RRF/MMR ohne
  Vektorsignal). Der semantische Pfad (ANN über den Neo4j-Vektor-Index bzw.
  `IVectorStore`) ist hier nicht vermessen.
- **recall@10 = 1,0** ist eine Eigenschaft der synthetischen Ground-Truth
  (distinktive Topic-Token), **keine** Aussage über reale Retrieval-Qualität.
- Einzel-Thread, Wall-Clock (`TimeProvider.System`); frühe Cases enthalten
  JIT-Aufwärmung (leicht erhöhtes P95 gegenüber einem warmen Lauf).

## Nächste Schritte (nutzerseitig, brauchen Infrastruktur)

- Gegen ein echtes Neo4j messen (gleicher Harness, `GRAPH_PROVIDER=neo4j`) für die
  Produktionslatenz auf dem indizierten Pfad.
- Den Semantik-Pfad mit einem Embedding-Provider + Vektor-Index vermessen
  (`IVectorStore`), um die ANN-Kosten bei großem N zu erfassen.
