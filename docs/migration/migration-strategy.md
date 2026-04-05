# Migration Strategy

## Migration units

Treat old platform migration as five independent unit types:
- code
- runtime metadata
- data
- hosts
- tooling and scripts

## Rules

1. do not migrate everything at once
2. freeze first-wave scope
3. prefer native rewrite for kernel and contracts
4. use compatibility seams only when they shorten risk, not when they hide complexity
5. verify one real old asset class early

## Suggested first wave

- new host
- new frontend shell
- module contracts
- demo module
- Authorization
- ConfigCenter
- Audit
- one metadata/import proof of concept

## Default postponed areas

- WinApp rewrite
- Studio rewrite
- file-service rewrite
- deep file migration
