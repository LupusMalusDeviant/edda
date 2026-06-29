# Sandboxing

Das **Sandboxing**-Projekt implementiert isolierte Ausführungsumgebungen für Code-Execution-Tools und die TDK-Engine. Zwei Sandbox-Typen: Docker-Container (für Shell/Python) und WebAssembly (für TDK-Validierung).

---

## Abhängigkeiten

```
Sandboxing → Core
```

Externe Pakete: `Docker.DotNet`, `Wasmtime`

---

## Verzeichnisstruktur

```
Sandboxing/
├── Docker/               ← Docker-basierte Sandbox
├── Wasm/                 ← WebAssembly-basierte Sandbox
├── NullSandboxFactory.cs ← Fallback wenn kein Docker
└── DependencyInjection/
```

---

## Docker/

### `DockerSandboxFactory.cs`
Implementiert `ISandboxFactory`. Erstellt kurzlebige Docker-Container als Sandbox.

**Container-Konfiguration:**
- Arbeitsverzeichnis `/workspace` (beschreibbar)
- Kein Netzwerk-Zugriff (Network=none)
- CPU- und Memory-Limits (konfigurierbar)
- `--security-opt no-new-privileges`
- Automatische Bereinigung nach Ausführung
- Python-Image konfigurierbar via `SANDBOX_PYTHON_IMAGE` (Standard: `python:3.12-slim`)

### `DockerSandbox.cs`
Implementiert `ISandbox` für einen einzelnen Container-Lifecycle:

```csharp
Task<SandboxResult> ExecuteAsync(string command, string? input, TimeSpan timeout, ct);
```

**Funktionsweise:**
1. Container mit konfigurierten Limits erstellen (Arbeitsverzeichnis: `/workspace`)
2. Script und Input als Dateien in den Container kopieren
3. Ausführung via `docker exec`: `python3 /workspace/script.py < /workspace/input.json`
4. Stdout/Stderr mit Timeout-Überwachung lesen
5. Container stoppen und entfernen

**SandboxResult:** `{ Stdout, Stderr, ExitCode, TimedOut }`

### `IDockerContainerOperations.cs` / `DefaultDockerContainerOperations.cs`
Abstrahiert `Docker.DotNet`-API für Testbarkeit. Enthält: `CreateContainerAsync`, `StartContainerAsync`, `ExecContainerAsync`, `RemoveContainerAsync`.

---

## Wasm/

### `WasmSandboxFactory.cs`
Implementiert `ISandboxFactory` für WebAssembly-basierte Isolation. Primär für TDK-Validierungs-Sandbox gedacht — kein Netzwerk, kein Dateisystem.

### `WasmSandbox.cs`
Führt WASM-Module via `Wasmtime` aus. Schneller Start (< 10ms) im Vergleich zu Docker-Containern.

### `IWasmScriptRunner.cs` / `DefaultWasmScriptRunner.cs`
Abstrahiert `Wasmtime.Engine` für Testbarkeit.

---

## `NullSandboxFactory.cs`
Implementiert `ISandboxFactory`. Gibt immer `SandboxResult` mit `ExitCode=-1` und Fehlermeldung zurück.

Wird verwendet wenn:
- Docker nicht verfügbar ist
- `SANDBOXING_ENABLED=false` gesetzt ist

Verhindert, dass der Agent beim Start abstürzt wenn kein Docker läuft.

---

## DependencyInjection/

### `SandboxingServiceExtensions.AddSandboxingServices(services, configuration)`
Registriert:
- `ISandboxFactory` → `DockerSandboxFactory` wenn Docker verfügbar, sonst `NullSandboxFactory`
- `IDockerContainerOperations` → `DefaultDockerContainerOperations`
- Optionale WASM-Factory wenn `WASM_SANDBOX_ENABLED=true`

---

## Umgebungsvariablen

| Variable | Standard | Beschreibung |
|---|---|---|
| `SANDBOXING_ENABLED` | `true` | Docker-Sandboxing aktivieren |
| `SANDBOX_MEMORY_MB` | `256` | Memory-Limit pro Container in MB |
| `SANDBOX_CPU_QUOTA` | `50000` | CPU-Quota (Mikrosekunden per 100ms) |
| `SANDBOX_TIMEOUT_SEC` | `30` | Timeout pro Ausführung in Sekunden |
| `SANDBOX_PYTHON_IMAGE` | `python:3.12-slim` | Python-Docker-Image für Sandbox-Container |
| `WASM_SANDBOX_ENABLED` | `false` | WASM-Sandbox aktivieren |

---

## Sicherheitsgarantien

- **Netzwerkisolation:** Container laufen mit `--network none`
- **Filesystem-Isolation:** Root-Filesystem read-only; nur `/workspace` beschreibbar
- **Resource-Limits:** CPU und Memory nach oben begrenzt
- **Privilege-Eskalation verhindert:** `--security-opt no-new-privileges`
- **Automatische Bereinigung:** Container werden nach Ausführung immer entfernt (auch bei Crash)
