---
id: tool-search-memory
title: search_memory Tool Guide
domain: tools.knowledge
type: Guideline
priority: Medium
tags: [tool, search-memory, memory, langzeitgedaechtnis, wissen, kontext]
concepts: [memory, search, langzeitgedaechtnis, wissen, kontext, suchen, gedaechtnis]
author: system
requires: [tool-list-memory]
---

## search_memory

### Beschreibung

Durchsucht das Langzeitgedächtnis (Wissensgraph) nach allem, was zu einer Anfrage bekannt ist, und kompiliert daraus den aktiven Kontext: relevante Wissensregeln und erkannte Konflikte. Der Agent sollte dieses Tool aufrufen, **bevor** er das Dateisystem durchsucht, um vorhandenes Wissen zu nutzen.

### Parameter

| Parameter | Typ | Pflicht | Beschreibung |
|-----------|-----|---------|-------------|
| query | string | Ja | Die Anfrage, zu der das Langzeitgedächtnis durchsucht werden soll |

### Verwendungsbeispiele

- Vor dem Lösen einer Aufgabe prüfen, was bereits bekannt ist → `query: "async programming in C#"`
- Nachschlagen, welches Wissen zu einem Thema gespeichert ist, statt direkt Dateien zu scannen → Gedächtnis durchsuchen

### Best Practices

- Als ersten Schritt aufrufen, bevor das Dateisystem durchsucht wird
- Die Anfrage konkret formulieren, damit der kompilierte Kontext möglichst relevant ist
