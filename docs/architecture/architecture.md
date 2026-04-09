# Architecture

## Goal

Build a modular platform kernel that can host reusable public modules and extension modules without editing kernel internals.

## Layers

### Host
- startup
- auth integration
- module discovery
- route/menu aggregation
- global middleware

### Kernel
- core abstractions
- eventing
- caching
- storage abstractions
- auditing abstractions
- configuration abstractions

### Module Contracts
- module metadata
- service registration
- route/menu/permission/config declarations
- migration/import hook

### Built-in Modules
- Authorization
- ConfigCenter
- Audit

### SDK
- server-side module SDK
- frontend module SDK
- templates and registration helpers

### Modeling
- strong-typed model semantics
- AI-assisted metadata authoring
- persistence adapters project model semantics into runtime storage

## Migration stance

Old platform assets do not all become modules.
Each old asset class must first be classified:
- Retire
- Rewrite
- Wrap
- Import
- Postpone

## First-wave boundaries

The first wave should not include:
- workflow redesign
- report designer redesign
- studio/winapp rewrite
- file-service rewrite
- deep business migration

## Metadata authoring

At the current stage, metadata is authored by AI and reviewed by humans.
We do not build a human-facing metadata designer first.

Reference:
- [ai-metadata-authoring-guide.md](ai-metadata-authoring-guide.md)
