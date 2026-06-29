---
Status: Vorgeschlagen
Datum: 2026-06-17
Autor: Eric Lenk
Konsultiert: —
---

# Generische, config-getriebene Quell-Connectoren (HTTP/REST und MCP)

Ersetzt: —

## Kontext und Problemstellung

ADR-0005 hat das config-getriebene Connector-Modell etabliert: ein `ConnectorDescriptor`
(Feldliste mit Typ/Pflicht/Default) rendert das UI automatisch, neue Quellen brauchen keinen UI-Code.
Bislang existierte aber nur der Git-Connector. Gefordert sind nun Batch-Importe ganzer GitLab-Gruppen
sowie beliebige Fremdquellen — Awork, Jira, generische REST-APIs und externe MCP-Server.

Eine Klasse pro Anbieter zu schreiben skaliert nicht und widerspricht dem „1 Modul, das alle Retrievals
abdeckt"-Ziel.

**Kernfrage:** Wie binden wir beliebige HTTP/REST- und MCP-Quellen an, ohne pro Anbieter eine eigene
Quell- und UI-Implementierung zu pflegen?

## Anforderungen

### Funktional
- Beliebige JSON-REST-API per Konfiguration anbinden (Listen-Endpoint, JSON-Pointer für Items und Felder, Pagination).
- Einen externen MCP-Server als Wissensquelle anzapfen (Tool-Aufruf → Inhalte).
- Eine ganze GitLab-Gruppe inkl. Untergruppen als Batch ingestieren.
- Base-URL und Token **pro Quell-Instanz** (mehrere Instanzen/Tokens nebeneinander).

### Nicht-funktional
- Kein Code je Vendor für den Normalfall; Spezialfälle über dünne Presets.
- Secrets ausschließlich server-seitig (Credential-Store), nie aus Request-Daten.
- Ohne Netzwerk unit-testbar (Mapping/Parsing als reine Funktionen).
- In die bestehende Ingestion-Pipeline einfügbar (gleicher Mapper, gleiche Relationenauflösung).

## Betrachtete Optionen

### Option 1: Generische HTTP- + MCP-Source mit Deskriptor-Config (+ dünne Vendor-Presets)
Eine `HttpApiSource` (SourceKind `custom-http`) und eine `McpKnowledgeSource` (SourceKind `mcp`), beide
rein über Settings konfiguriert; Vendor-Presets (`jira`, `awork`, `gitlab-group`) sind dünne Connectoren,
die diese Quellen mit fertigen Defaults füllen. Clients werden pro Lauf über Factories gebaut.

**Positiv:**
- Neue Quelle = neue Konfiguration oder ein dünnes Preset, kein UI-/Pipeline-Code.
- Ein einziger Ingestion-/Mapping-Pfad für alle Quellen.
- Per-Instanz-Clients (Base-URL/Token) sauber über `IHttpClientFactory`-Factories.

**Negativ:**
- Generisches JSON-Mapping passt nicht auf jede exotische API (mitigiert durch Presets + den generischen Connector als Fallback).
- Vendor-Spezifika (Pagination, Auth-Kodierung wie Jiras `base64(email:token)`) müssen je Preset gepflegt werden.

### Option 2: Eine bespoke Connector-Klasse je Anbieter
Pro Quelle (Jira, Awork, …) eine vollständige eigene `IIngestionSource` + Connector.

**Positiv:**
- Maximale Passgenauigkeit je Anbieter.

**Negativ:**
- O(n) Code/Tests/Wartung mit der Zahl der Anbieter; widerspricht dem Generizitätsziel.

### Option 3: Nur die bestehende `IIngestionSource` minimal erweitern
Git-Source um ein paar Felder ergänzen, keine generische HTTP/MCP-Abstraktion.

**Positiv:**
- Kleinster Eingriff.

**Negativ:**
- Deckt HTTP/MCP-Quellen gar nicht ab; verschiebt das Problem nur.

## Entscheidung

Gewählt wird **Option 1**: eine generische `HttpApiSource` und eine `McpKnowledgeSource`, konfiguriert über
`ConnectorDescriptor`, plus dünne Presets (`jira`, `awork`) und der GitLab-Gruppen-Connector
(`gitlab-group`). Per-Instanz-Clients entstehen über `IGitLabClientFactory` bzw.
`IExternalMcpClientFactory`. So sind neue Quellen Konfiguration statt Code, während der generische
Connector der garantierte Weg für Abweichungen bleibt.

## Konsequenzen

### Positiv
- „1 Modul per Config" erreicht: Awork/Jira/REST/MCP über Konfiguration.
- Einheitliche Pipeline, einheitliche Relationenauflösung, einheitliche Secret-Behandlung.
- Reine Mapping-/Parsing-Funktionen sind ohne Infrastruktur testbar.

### Negativ
- Presets kapseln Vendor-Annahmen (Endpoint/Pagination/Auth), die sich ändern können → müssen gepflegt werden.
- Das MCP-Ergebnis-Mapping ist notwendig generisch (ein Item je Text-Content-Block), da die Tool-Ausgabe server-definiert ist.

## Weitere Informationen

Knüpft an ADR-0005 an und erfüllt dessen „Custom-HTTP als Realitätstest". Secrets folgen dem Schema
`{userId}:source:{instanceId}:{feld}`. Verwaltung der Instanzen zusätzlich über die REST-API
(`/api/connectors`, `/api/sources`).
