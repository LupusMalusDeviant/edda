# Retrieval-Benchmark — Baseline (Checkpoint 2)

Diese Datei hält die **Retrieval-Baseline** fest, die als Pflichtgrundlage für Phase 3 dient —
insbesondere für **B1** (Retrieval-Ranking-Änderung): jede Ranking-Änderung muss gegen diese Zahlen
gemessen werden und **messbar neutral oder besser** ausfallen, sonst Revert.

Gemessen wird die 4-Phasen-Kontext-Kompilierung (`IKnowledgeGraph.CompileContextAsync`) über den
`AkgBenchmarkRunner` (`IBenchmarkRunner`, Endpoint `POST /api/akg/benchmark`).

## Konfiguration (reproduzierbar)

| Einstellung | Wert |
|---|---|
| `GRAPH_PROVIDER` | `memory` (InMemory-Graph, kein Neo4j nötig) |
| `EMBEDDING_PROVIDER` | `null` → **Keyword-only** (die semantische Phase + Head-Vorfilterung entfallen; kein API-Key nötig) |
| Cutoff `k` | 5 |
| Wissensbasis | die 12 mitgelieferten `knowledge/`-Regeln (Baseline-Seed) |
| Datensatz | `bundled-knowledge-baseline`, 11 Fälle (siehe unten) |
| Datum | 2026-07-02 |

> **Wichtig:** Diese Baseline ist **keyword-only** (ohne Embedding-Provider). Sie ist so gewählt, weil
> sie ohne Secrets/externe Provider exakt reproduzierbar ist. Ein B1-Vergleich muss unter **derselben**
> Konfiguration laufen; ein Lauf mit echtem Embedding-Provider erzeugt eine separate Baseline.

## Reproduktion

```bash
GRAPH_PROVIDER=memory EMBEDDING_PROVIDER=null dotnet run --project src/Web
# in einem zweiten Terminal (Loopback = lokaler Admin, kein Token nötig):
curl -s -X POST -H "Content-Type: application/json" \
     -d @docs/benchmark-baseline-dataset.json \
     "http://127.0.0.1:8080/api/akg/benchmark?k=5"
```

## Baseline-Kennzahlen (Aggregat über 11 Fälle, k=5)

| Metrik | Wert |
|---|---|
| **Recall@5** | **0.727** |
| **Precision@5** | **0.164** |
| **MRR** | **0.636** |
| **nDCG@5** | **0.660** |
| Latenz p50 | 20.9 ms |
| Latenz p95 | 53.4 ms |
| Ø geschätzte Tokens/Anfrage | 2858 |

Precision@5 ist per Konstruktion niedrig: die meisten Fälle haben nur 1 erwartete Regel, bei k=5 sind
also höchstens 1/5 = 0.2 der Slots relevant. Aussagekräftiger für den B1-Vergleich sind **Recall@5**,
**MRR** und **nDCG@5**.

## Fälle (Ground Truth)

Der Datensatz liegt als `docs/benchmark-baseline-dataset.json` bei. Ergebnis pro Fall:

| Fall | Query (verkürzt) | Erwartet | Recall@5 | MRR |
|---|---|---|---|---|
| async | async/await/deadlock/Result/Wait | async-await-patterns, no-blocking-async | 1.00 | 1.00 |
| except | exception/except pass/swallow | no-bare-except | 1.00 | 0.50 |
| debug | console.log/debugger/debug | no-leftover-debug | 1.00 | 1.00 |
| eval | eval/exec/code injection | no-eval-exec | 1.00 | 1.00 |
| secrets | password/api key/secret | no-plaintext-secrets | **0.00** | 0.00 |
| sqli | sql injection/concatenation | no-sql-string-concat | 1.00 | 1.00 |
| api | REST API design/versioning | world-api-design | 1.00 | 1.00 |
| data | database/data access/storage | world-data-patterns | 1.00 | 0.50 |
| oop | OOP/inheritance/polymorphism | world-oop-principles | 1.00 | 1.00 |
| secprin | security principles/least privilege | world-security-principles | **0.00** | 0.00 |
| arch | software architecture/layers | world-software-architecture | **0.00** | 0.00 |

## Beobachtungen (Startpunkt für B1)

- **8 von 11 Fällen** werden vollständig abgedeckt (Recall@5 = 1.0), meist auf Rang 1 (MRR = 1.0).
- **3 Fälle verfehlen** die erwartete Regel komplett (`secrets`, `secprin`, `arch`): reines
  Keyword-Matching findet die thematisch passende Regel nicht in den Top-5, obwohl die Begriffe in
  Tags/Concepts stehen. Das ist die erwartete Schwäche ohne semantische Phase — genau der Hebel, an dem
  eine Embedding-gestützte oder verbesserte Ranking-Strategie (B1) messbar zulegen sollte.
- Latenz ist mit ~21 ms p50 unkritisch (InMemory, keyword-only).

## B1 — Vorher/Nachher (Keyword-Tokenisierung + Sättigung)

Änderung: `KeywordScorer` matcht Tags/Concepts jetzt gegen **ganze Task-Token** (statt `string.Contains`)
und deckelt den Score per `log(1 + matchCount)` (Spec `docs/plans/0003-b1-keyword-tokenisierung.md`,
Stufe 1). Gleiche Konfiguration wie oben (memory, keyword-only, k=5, derselbe Datensatz).

| Metrik | Baseline (vorher) | B1 (nachher) | Δ |
|---|---|---|---|
| Recall@5 | 0.727 | **0.818** | **+0.091** |
| Precision@5 | 0.164 | **0.182** | +0.018 |
| MRR | 0.636 | **0.705** | **+0.069** |
| nDCG@5 | 0.660 | **0.733** | **+0.073** |
| Latenz p50 | 20.9 ms | 18.9 ms | −2.0 ms |

**Ergebnis: messbar besser auf allen vier IR-Metriken** (Akzeptanz „neutral oder besser" erfüllt).
Per-Fall: `secprin` (world-security-principles) verbessert sich von Recall 0.0 → 1.0, `except` von
MRR 0.50 → 1.00. Es verbleiben zwei Fehltreffer (`secrets`, `arch`) — reine Keyword-Grenzen, die erst
die semantische Phase (Embeddings) schließen dürfte. Latenz unverändert unkritisch.

## B5 — Vorher/Nachher (concepts-Reparatur + Query-Expansion)

Zwei Änderungen (Spec `docs/plans/0018-b5-query-expansion.md`), gemessen unter derselben
Konfiguration (memory, keyword-only, k=5, derselbe Datensatz):

1. **concepts-Reparatur** (Default-Wirkung): Der `concepts`-Scoring-Zweig des `KeywordScorer`
   (+3 je Konzept-Treffer) war im Produktivpfad tot verdrahtet — `concepts` wurden weder
   persistiert noch gemappt (F1-Muster), aus dem Graphen geladene Regeln hatten
   `WhenRelevant = null`. Die Kette (Upsert-SET → NodeMapper → InMemory) ist repariert.
2. **Query-Expansion** (opt-in, `RETRIEVAL_QUERY_EXPANSION_TERMS`, Default 0 = aus):
   deterministische Ko-Okkurrenz-Expansion über die kuratierten Tags/Konzepte, nur im
   Keyword-Pfad, Treffer-Gewicht `RETRIEVAL_QUERY_EXPANSION_WEIGHT` (Default 0.5).

| Metrik | B1 (vorher) | + concepts-Fix (Default) | + Expansion (TERMS=3) |
|---|---|---|---|
| Recall@5 | 0.818 | **0.909** | 0.909 |
| Precision@5 | 0.182 | **0.200** | 0.200 |
| MRR | 0.705 | **0.795** | 0.795 |
| nDCG@5 | 0.733 | **0.824** | 0.824 |

**Ergebnis:** Die concepts-Reparatur ist **messbar besser auf allen vier IR-Metriken** — der
`secrets`-Fall (no-plaintext-secrets, Konzepte `password`/`api-key`/`secret`/…) springt von
Recall 0.0 → 1.0 (MRR 1.0). Verbleibender Fehltreffer: nur noch `arch` (1 von 11). Die
**Expansion ist auf diesem Datensatz exakt neutral** (identische Zahlen, kein Noise-Regress):
Die Fälle, die der concepts-Fix nicht löst, haben in der kleinen Baseline-Wissensbasis keine
Ko-Okkurrenz-Brücke. Empfehlung: **Default bleibt 0 (aus)** — das Feature ist für reichere,
stärker vernetzte Wissensbasen gedacht und dort risikofrei zuschaltbar.
