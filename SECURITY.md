# Sicherheitsrichtlinie

Danke, dass du hilfst, Edda sicher zu halten. Dieses Dokument beschreibt, wie du
Sicherheitslücken meldest und was du von uns erwarten kannst.

## Unterstützte Versionen

Edda ist ein aktiv entwickeltes Projekt ohne Long-Term-Support-Zweige. Sicherheits-Fixes
fließen in den `main`-Branch bzw. das jeweils aktuelle Release. Betreibe Edda möglichst nah
am aktuellen Stand.

| Version            | Unterstützt |
|--------------------|:-----------:|
| `main` / neuestes Release | ✅ |
| ältere Releases    | ❌ |

## Eine Sicherheitslücke melden

**Bitte melde Sicherheitslücken nicht über öffentliche GitHub-Issues, Pull Requests oder
Diskussionen.** Öffentliche Meldungen setzen andere Nutzer einem Risiko aus, bevor ein Fix
verfügbar ist.

Nutze stattdessen den privaten Meldekanal von GitHub:

1. Öffne die Registerkarte **Security** des Repositorys.
2. Klicke auf **„Report a vulnerability"** ([Private Vulnerability Reporting][pvr]).
3. Beschreibe das Problem so konkret wie möglich.

[pvr]: https://github.com/LupusMalusDeviant/edda/security/advisories/new

Falls du keinen Zugriff auf den privaten Kanal hast, eröffne ein Issue **ohne technische
Details** mit der Bitte um einen privaten Kontaktweg — wir melden uns dann zurück.

### Was in die Meldung gehört

- Betroffene Komponente (z. B. MCP-Server, REST-API `/api/*`, Blazor-UI, Ingestion,
  TDK-Sandbox, Credential-Store) und, wenn bekannt, Datei/Zeile oder Endpunkt.
- Art der Schwachstelle (z. B. AuthN/AuthZ-Umgehung, Injection, SSRF, Path-Traversal,
  Secret-Leak, Sandbox-Ausbruch).
- Schritt-für-Schritt-Reproduktion oder ein Proof-of-Concept.
- Betroffene Version/Commit und relevante Konfiguration (`.env`-Werte **redigiert**, keine
  echten Secrets im Report).
- Einschätzung der Auswirkung (Vertraulichkeit / Integrität / Verfügbarkeit).

## Ablauf & Reaktionszeiten

Dies ist ein von Freiwilligen gepflegtes Projekt — die Zeiten sind Zielwerte, keine Zusage:

- **Erstbestätigung:** innerhalb von 5 Werktagen.
- **Erste Einschätzung (Triage):** innerhalb von 10 Werktagen.
- **Fix & Coordinated Disclosure:** nach Schweregrad abgestimmt; wir halten dich auf dem
  Laufenden und nennen dich auf Wunsch im Advisory als Meldenden.

Bitte gib uns eine angemessene Frist zur Behebung, bevor du Details veröffentlichst
(Responsible / Coordinated Disclosure).

## Sicherheitsarchitektur (Kontext für Melder)

Edda ist auf einen **lokal-only, default-deny**-Betrieb ausgelegt. Für die Bewertung von
Meldungen ist folgender Kontext hilfreich:

- **MCP-Server ist read-only per Default:** Nur `search_memory` und `list_memory` sind
  freigegeben. Schreib-Tools (`manage_memory`, `manage_userdata`, `manage_learnings`) sind
  über MCP **grundsätzlich blockiert** — nur ein explizites `MCP_ALLOW_WRITE_TOOLS=true`
  hebt das auf. Eine Umgehung dieser Grenze ist ein relevanter Befund.
- **Bind & Auth:** Default-Bind ist `127.0.0.1`. Ein Remote-Bind (`0.0.0.0`) verlangt einen
  gesetzten `EDDA_AUTH_TOKEN`; ohne Token bleibt `/mcp`/`/api/*` auf Loopback beschränkt.
  Auth-Umgehungen bei Remote-Bind sind relevant.
- **Secrets:** Credentials liegen server-seitig im verschlüsselten Credential-Store; Logs
  werden durch einen Secret-Redactor gefiltert. Secret-Leaks (Logs, Fehler-Responses, UI)
  sind relevant.
- **TDK-Sandbox:** Validator-Skripte laufen gesandboxed (Docker/Wasm). Ausbrüche aus der
  Sandbox sind relevant.

### In der Regel *nicht* im Scope

- Befunde, die einen bereits kompromittierten Host oder physischen Zugriff voraussetzen.
- Fehlende Härtung in einer bewusst unsicheren Konfiguration (z. B. Remote-Bind **ohne**
  gesetzten `EDDA_AUTH_TOKEN`, obwohl die Doku davor warnt).
- Schwachstellen ausschließlich in Drittanbieter-Diensten (Neo4j/Memgraph, LLM-/Embedding-
  Provider) ohne Bezug zu Eddas Code — melde diese bitte beim jeweiligen Projekt.
- Selbst verursachte Denial-of-Service durch absichtlich extreme Eingaben.

Vielen Dank für deinen Beitrag zur Sicherheit von Edda.
