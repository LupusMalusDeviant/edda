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
