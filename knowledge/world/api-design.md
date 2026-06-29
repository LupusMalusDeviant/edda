---
id: world-api-design
title: API Design Principles
domain: world
type: WorldKnowledge
priority: Low
tags: [rest, graphql, grpc, idempotency, versioning, openapi, hateoas]
concepts: [rest, graphql, grpc, idempotency, versioning, openapi, hateoas, api-design]
author: system
requires: [world-oop-principles]
---

## API Design Principles

### RESTful API Design

REST uses HTTP verbs semantically: **GET** (read, safe, idempotent), **POST** (create, non-idempotent), **PUT** (replace, idempotent), **PATCH** (partial update), **DELETE** (remove, idempotent).

Resource URIs are nouns, not verbs: `/api/users/42` not `/api/getUser`. Collections are plural nouns.

### Idempotency

A request is idempotent if repeated identical calls produce the same result. Clients can safely retry idempotent requests after network failures. Use idempotency keys for POST operations that must not be duplicated (e.g., payment processing).

### Versioning Strategies

- **URI versioning**: `/api/v1/users` — simple, visible, easy to route.
- **Header versioning**: `Accept: application/vnd.myapi.v1+json` — cleaner URIs, harder to test in browser.
- **Query parameter**: `/api/users?version=1` — least preferred.
Never introduce breaking changes without incrementing the version.

### OpenAPI / Swagger

Document your API with an OpenAPI specification. Enables auto-generated clients, interactive docs (Swagger UI), and contract testing. Define request/response schemas precisely; use `$ref` for reuse.

### gRPC

Protocol Buffers-based binary RPC framework. Strongly typed contracts via `.proto` files. Supports streaming. Preferred for internal service-to-service communication where performance matters.

### GraphQL

Client-specified queries: clients request exactly the fields they need, avoiding over- and under-fetching. Best suited for public APIs with diverse clients. Adds complexity (N+1 query problem requires DataLoader).

### Key Principles

- **Consistency**: Use the same naming, error format, and pagination pattern everywhere.
- **Pagination**: Prefer cursor-based pagination over offset for large datasets.
- **Error responses**: Return structured errors with a machine-readable code and human-readable message.
- **HATEOAS**: Include hypermedia links in responses to guide client navigation (common in mature REST APIs).
- **Rate limiting**: Protect the API; return `429 Too Many Requests` with `Retry-After` header.
