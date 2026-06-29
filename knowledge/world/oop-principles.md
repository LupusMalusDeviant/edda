---
id: world-oop-principles
title: Object-Oriented Programming Principles
domain: world
type: WorldKnowledge
priority: Low
tags: [oop, solid, classes, inheritance, encapsulation, polymorphism, design-patterns]
concepts: [oop, solid, encapsulation, inheritance, polymorphism, abstraction, design-patterns]
author: system
requires: [world-data-patterns, world-api-design]
---

## Object-Oriented Programming Principles

### The Four Pillars of OOP

**Encapsulation**: Bundle data and the methods that operate on it inside a class. Hide internal state; expose only a clean public interface. This reduces coupling and makes internals safe to change.

**Inheritance**: Derive new classes from existing ones to reuse and extend behavior. Prefer shallow hierarchies — deep inheritance chains become fragile. Favor *composition over inheritance* when flexibility is needed.

**Polymorphism**: Objects of different types can be treated through a shared interface. This enables the Open/Closed Principle: code is open for extension, closed for modification.

**Abstraction**: Model the essential characteristics of a domain concept while hiding irrelevant details. Interfaces and abstract classes are the primary tools.

### SOLID Principles

- **S — Single Responsibility**: A class has exactly one reason to change. Extract concerns into separate classes.
- **O — Open/Closed**: Extend behavior via new classes/interfaces, not by modifying existing code.
- **L — Liskov Substitution**: Derived types must be substitutable for their base types without altering correctness.
- **I — Interface Segregation**: Prefer many small, focused interfaces over one large general-purpose one.
- **D — Dependency Inversion**: Depend on abstractions (interfaces), not on concrete implementations.

### Common Design Patterns

- **Factory / Abstract Factory**: Decouple object creation from usage.
- **Strategy**: Encapsulate interchangeable algorithms behind a common interface.
- **Observer**: One-to-many event notification without tight coupling.
- **Decorator**: Add responsibilities to objects dynamically without subclassing.
- **Repository**: Isolate domain logic from data-access concerns.
