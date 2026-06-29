---
id: world-security-principles
title: Software Security Principles
domain: world
type: WorldKnowledge
priority: Low
tags: [owasp, zero-trust, least-privilege, defense-in-depth, threat-modeling, authentication]
concepts: [owasp, zero-trust, least-privilege, defense-in-depth, threat-modeling, authentication, authorization]
author: system
requires: [security-no-plaintext-secrets]
---

## Software Security Principles

### Defense in Depth

Apply multiple, independent layers of controls. No single control is assumed to be perfect. If one layer fails, others compensate. Examples: input validation + parameterized queries + WAF.

### Principle of Least Privilege

Grant the minimum permissions necessary for a task. Services, users, and processes should not accumulate rights "just in case." Revoke access immediately when no longer needed.

### Zero Trust

"Never trust, always verify." Authenticate and authorize every request, regardless of network location. Assume breach: segment networks, encrypt traffic internally, and enforce short-lived credentials.

### OWASP Top 10 (key items)

- **Injection** (SQL, Command, LDAP): Always use parameterized queries / prepared statements.
- **Broken Authentication**: Enforce MFA, secure session management, and credential storage with bcrypt/Argon2.
- **Sensitive Data Exposure**: Encrypt at rest and in transit; never log secrets.
- **Security Misconfiguration**: Disable default accounts, apply security headers, review dependency configurations.
- **Insecure Deserialization**: Validate and whitelist deserialized types; avoid deserializing untrusted data.

### Threat Modeling

Identify assets, entry points, and potential threats early (STRIDE: Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege). Prioritize mitigations by risk.

### Authentication vs. Authorization

- **Authentication**: Who are you? (Identity verification via credentials, tokens, certificates)
- **Authorization**: What are you allowed to do? (Role-based or attribute-based access control)
- Always validate on the server side; never rely on client-side controls alone.

### Cryptography Basics

Use industry-standard libraries; never roll your own crypto. Prefer AES-256-GCM for symmetric encryption, RSA-2048+/ECDSA for asymmetric, and SHA-256+ for hashing. Rotate keys regularly.
