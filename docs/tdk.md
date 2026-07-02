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

## Validator-Skript-Format

Ein Validator liest `TdkValidatorInput` (Code, Sprache, RuleId, UserMessage) von stdin als JSON
und schreibt `TdkValidatorOutput` (`pass`, `violations[]`) als JSON nach stdout. Siehe die
Regeln unter `knowledge/` als Beispiele.

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
