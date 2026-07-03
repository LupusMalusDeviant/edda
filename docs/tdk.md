# TDK — Test-Driven Knowledge

TDK ist das Alleinstellungsmerkmal gegenüber reinen Memory-Schichten: Regeln können generierten
Code **aktiv ablehnen**, statt Konventionen nur zu beschreiben. Eine Regel trägt dazu ein
`validatorScript` (Python), das gegen Code-Blöcke ausgeführt wird.

## Ablauf (`tdk_validate` / Seite `/tdk`)

1. Kontext für das Thema kompilieren (`IKnowledgeGraph.CompileContextAsync`) → aktive Regeln.
2. Nur Regeln mit `ValidatorScript` behalten; Code-Blöcke aus der Eingabe extrahieren.
3. Pro (Regel × Block) das Validator-Skript **isoliert in der Sandbox** ausführen
   (`ISandboxFactory`), JSON-Ein-/Ausgabe (`TdkValidatorInput`/`TdkValidatorOutput`).
4. Verstöße (RuleId, Severity, Message) sammeln; Pass/Fail fließt in den Konfidenz-Store
   (`IRuleConfidenceStore`) und optional in den Feedback-Loop.

## Sandbox (Pflicht für Validatoren)

`TDK_SANDBOX_TYPE` wählt die Isolation:

| Wert | Factory | Voraussetzung |
|------|---------|---------------|
| `docker` (Default) | DockerSandboxFactory | erreichbarer Docker-Daemon (`/var/run/docker.sock` mounten); Netz `--network=none` |
| `wasm` | WasmSandboxFactory | Python 3.12 auf dem Host (lokaler Subprozess) |
| `null` | NullSandboxFactory | keine — Validatoren liefern „nicht konfiguriert", `tdk_validate` läuft, meldet aber keine Verstöße |

Ohne Docker/Python schlägt die Sandbox-Erstellung fehl; die UI-Seite `/tdk` zeigt den Fehler
lesbar an. Für reine Retrieval-Nutzung ohne Code-Validierung ist `null` ausreichend.

### Ressourcen-Grenzen und Restrisiko der `wasm`-Sandbox

Die `docker`-Sandbox ist gehärtet (256 MB RAM, 50 % CPU, `--network=none`) und die erste Wahl für
**nicht vertrauenswürdige** Validatoren. Der `wasm`-Pfad startet Python als lokalen Subprozess **ohne
Container-Isolation**; er ist mit **Best-Effort-Grenzen** abgesichert, die einen versehentlichen
Denial-of-Service auf dem Host eindämmen, aber **keine** vollständige Isolation bieten:

- **Hartes Wall-Clock-Kill:** Nach dem Timeout wird der gesamte Prozessbaum beendet (`Kill(entireProcessTree)`).
- **Niedrige Priorität:** Der Prozess läuft mit `BelowNormal`, damit ein CPU-lastiges Skript den Host nicht aushungert.
- **Ausgabe-Limit:** stdout/stderr werden je auf ~1 MB gedeckelt; darüber hinaus wird der Lauf abgebrochen (Schutz gegen Ausgabe-Flut).
- **Linux `ulimit`:** Zusätzlich werden CPU-Zeit (≈ Timeout), Dateigröße (10 MB) und Adressraum (1 GiB) per `ulimit` begrenzt.

**Restrisiko:** Es gibt **keine** cgroup-basierte RAM-/CPU-Isolation, keine Netzwerk-Sperre und keine
Dateisystem-Isolation im `wasm`-Modus — ein bösartiges Skript kann innerhalb der obigen Grenzen weiterhin
Host-Ressourcen und -Dateien im Rahmen der Prozessrechte nutzen. Für nicht vertrauenswürdige Validatoren
**`TDK_SANDBOX_TYPE=docker` verwenden**; `wasm` ist für lokale, kuratierte Validatoren gedacht, wenn kein
Docker verfügbar ist. Vollständige cgroup-/Namespace-Isolation ist bewusst außerhalb des Umfangs dieses Pfads.

### Batch-Ausführung (`TDK_SANDBOX_BATCH`)

Standardmäßig erzeugt TDK **einen Container pro (Regel × Block)**. Mit `TDK_SANDBOX_BATCH=true`
läuft stattdessen **ein Container pro Validierung**: ein Runner-Skript bekommt alle Jobs als JSON und
führt jeden Validator als Subprozess aus (frische stdin/stdout — Verhalten identisch zum Einzellauf).
Da der Container-Start der teure Teil ist, sinkt die Latenz bei mehreren Paaren deutlich (~15
Container → 1).

**Trade-offs (Default AUS):**
- Die Validatoren eines Batches teilen sich einen Container (gleiche Vertrauensdomäne = eigene
  Wissensbasis). Wer harte Isolation je Validator braucht, bleibt beim Default.
- Der Batch läuft im Container-Timeout (10 s); viele/langsame Validatoren können den Batch killen —
  betroffene Jobs werden dann als Engine-Fehler gemeldet. Batch ist für schnelle Validatoren gedacht.
- Verstöße, Konfidenz-Buchung und Ergebnis-Cache sind pro Job identisch zum Per-Paar-Modus.

## Validator-Skript-Format

Ein Validator liest `TdkValidatorInput` (Code, Sprache, RuleId, UserMessage) von stdin als JSON
und schreibt `TdkValidatorOutput` (`pass`, `violations[]`) als JSON nach stdout. Siehe die
Regeln unter `knowledge/` als Beispiele.

## Helper-Modul `tdk` (optional)

Damit Validatoren nicht jedes Mal JSON-I/O und das Violation-Format neu bauen, legt die Sandbox
zur Laufzeit ein mitgeliefertes Python-Modul **`tdk.py` neben das Skript** (Docker:
`/workspace/tdk.py`; `wasm`: im selben Temp-Verzeichnis). Ein Validator kann es importieren:

```python
from tdk import validator, violation

@validator(languages=["python"])
def check(code, ctx):
    for m in ctx.finditer(r"except:\s*pass"):
        yield violation("Bare except: pass swallows errors",
                        line=ctx.line_of(m), severity="warning",
                        suggestion="Catch a specific exception and handle it.")
```

Das Modul übernimmt Einlesen von stdin, Aufruf der registrierten Validatoren und das Schreiben von
`{pass, violations}` nach stdout. API-Überblick:

| Baustein | Zweck |
|----------|-------|
| `@validator(languages=None)` | Registriert eine Prüffunktion `f(code, ctx)`; `languages` begrenzt sie optional auf Block-Sprachen. |
| `violation(message, *, line=None, severity="error", suggestion=None)` | Baut ein Violation-Dict; die `rule_id` ergänzt der Runner aus der Eingabe. |
| `ctx.code` / `ctx.language` / `ctx.rule_id` / `ctx.user_message` | Die Eingabefelder. |
| `ctx.finditer(pattern, flags=0)` | `re.finditer` über den Code-Block. |
| `ctx.line_of(match_or_pos)` | 1-basierte Zeilennummer eines Treffers/Offsets. |
| `ctx.python_ast()` | Parst den Block mit dem `ast`-Modul (nur Python-Blöcke). |

**Roh-stdin/stdout bleibt gültig:** Ein Skript, das `tdk` nie importiert, verhält sich exakt wie
zuvor. Wirft ein Validator eine Ausnahme, schreibt das Modul den Traceback nach stderr und beendet
sich mit ExitCode 1 — die Engine behandelt das als Validator-/Engine-Fehler und bucht **keine**
Konfidenz (analog zu Timeout/Crash), verfälscht die Regel-Konfidenz also nicht.

## Validator-Selbsttests (`validatorFixtures`)

Eine Regel kann im Frontmatter Selbsttest-Fixtures tragen — Codebeispiele, die der eigene
Validator akzeptieren bzw. ablehnen muss:

```yaml
validatorFixtures:
  pass:
    - |
      await credentialStore.StoreAsync("api-key", key, ct);
  fail:
    - |
      password = "hunter2"
```

Ein Prüf-Lauf führt den Validator gegen seine Fixtures aus. Die Regel gilt nur als **verifiziert**,
wenn *alle* `pass`-Snippets keinen Verstoß erzeugen und *alle* `fail`-Snippets mindestens einen.
So testet „Test-Driven Knowledge" auch die Regel selbst.

Auslöser:
- **UI:** Button „Fixtures prüfen" auf der Seite `/tdk` (`ITdkFixtureVerifier`).
- **Startup:** `TDK_FIXTURE_SELFTEST=true` prüft alle Regeln beim Start; `TDK_FIXTURE_SELFTEST_STRICT=true`
  lässt den Start bei echten Mismatches fehlschlagen (Fail-Fast für CI). Ohne verfügbare Sandbox
  (z. B. `null`) wird der Prüf-Lauf sauber übersprungen.

Fixtures sind **Authoring-Metadaten**: Sie werden aus dem Markdown geparst, aber **nicht** in den
Graphen persistiert und spielen zur Laufzeit von `tdk_validate` keine Rolle.

## Schweregrade (`severity`)

Jeder Verstoß trägt einen `severity`-Wert. Die drei Stufen haben eine feste Bedeutung:

| Wert | Bedeutung | Erwartung an den Agenten |
|------|-----------|--------------------------|
| `error` | **blockierend** — die Antwort ist so nicht akzeptabel | muss behoben werden |
| `warning` | sollte behoben werden, aber nicht blockierend | nach Möglichkeit beheben |
| `info` | reiner Hinweis | zur Kenntnis nehmen |

Der Feedback-Formatter (`TdkFeedbackFormatter`) sortiert die Verstöße **nach Schweregrad**
(zuerst `error`, dann `warning`, dann `info`; unbekannte Stufen zuletzt) und stellt der Liste eine
**Zählung pro Stufe** voran (z. B. „**3 violation(s)** — 2 error, 1 warning, 0 info."). Innerhalb
einer Stufe bleibt die ursprüngliche Reihenfolge erhalten. So sieht der Agent (und die
`tdk_validate`-Antwort) sofort, was zuerst zu fixen ist. Nicht erkannte `severity`-Werte werden als
`other` gezählt und ans Ende sortiert.
