# Sandboxing (TDK-Ausführung)

## Zweck

Führt den von der TDK-Validierung benötigten Code **isoliert** aus — Python-Validator-Skripte und
.NET-Builds, die generierten Code gegen die Wissensbasis prüfen. Drei austauschbare Strategien hinter
`ISandboxFactory`, gewählt über `TDK_SANDBOX_TYPE`: **docker** (Container, braucht den gemounteten
Docker-Socket), **wasm** (lokaler Python-Subprozess) und **null** (deaktiviert). Ohne diese Schicht
könnte TDK keinen Fremdcode sicher ausführen.

## Dateien

| Pfad | Rolle |
|------|-------|
| `src/Sandboxing/Docker/DockerSandbox.cs` | Führt ein Skript in einem Wegwerf-Container aus. |
| `src/Sandboxing/Docker/DockerSandboxFactory.cs` | Erzeugt Docker-Sandboxes (Python-Validatoren). |
| `src/Sandboxing/Docker/DefaultDockerContainerOperations.cs` | Docker-Operationen (Pull/Run/Logs/Rm) über Docker.DotNet. |
| `src/Sandboxing/Docker/IDockerContainerOperations.cs` | Abstraktion der Container-Ops (testbar). |
| `src/Sandboxing/Docker/DotNetBuildSandbox.cs` + `…Factory.cs` | Sandbox für `dotnet build`-Validierung. |
| `src/Sandboxing/Docker/DefaultDotNetBuildContainerOps.cs` + `IDotNetBuildContainerOps.cs` | .NET-Build-Container-Ops. |
| `src/Sandboxing/Wasm/WasmSandbox.cs` + `…Factory.cs` | Sandbox via lokalem Skript-Runner. |
| `src/Sandboxing/Wasm/DefaultWasmScriptRunner.cs` + `IWasmScriptRunner.cs` | Lokaler Python-Subprozess. |
| `src/Sandboxing/NullSandboxFactory.cs` | No-Op-Sandbox (Sandboxing aus). |
| `src/Sandboxing/DependencyInjection/SandboxingServiceExtensions.cs` | Wählt die Factory per `TDK_SANDBOX_TYPE`. |

## Abhängigkeiten

### Intern
- **Core** — `ISandboxFactory`, `SandboxResult`.

### Extern (Packages)
- `Docker.DotNet` — Steuerung des Docker-Daemons (nur docker-Strategie).

## Öffentliche API / Interface

- `ISandboxFactory` — erzeugt eine Sandbox-Instanz für einen Ausführungslauf.
- Ausführung liefert ein `SandboxResult` (Erfolg, Exit-Code, stdout/stderr) zurück; Fehler werden als
  Ergebnis gemeldet, nicht geworfen.
- Auswahl: `TDK_SANDBOX_TYPE = docker | wasm | null` (Default im Compose: `docker`).

## Datenfluss / Call-Flow

1. Die TDK-Engine (Feature *Agent-Tools & TDK*) ruft `ISandboxFactory`, um einen Validator auszuführen.
2. Die gewählte Factory startet die Ausführung — Docker-Container, lokaler Python-Subprozess oder No-Op.
3. Das `SandboxResult` (Pass/Fail + Logs) fließt zurück in die TDK-Bewertung.

## Offene Fragen / TODOs

- Die docker-Strategie benötigt den gemounteten `/var/run/docker.sock` (siehe `docker-compose.yml`); ohne
  Socket sollte produktiv auf `wasm` oder `null` gewechselt werden.
