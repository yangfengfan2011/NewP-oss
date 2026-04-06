# Module Contract

## Server-side contract

Each module should provide:
- module id
- display name
- version
- service registration entry
- permission declarations
- menu/route declarations
- config declarations
- migration/import hook if applicable

## Frontend-side contract

Each frontend module should provide:
- module id
- route registrations
- menu registrations
- permission point declarations
- i18n registrations
- page mount entry

## Contract rules

- host composes, modules declare
- modules should not mutate shell internals directly
- contracts should be serializable where practical
- any compatibility adapter must be explicit in the contract or migration layer

## Guardrails

- Host only composes and loads modules; it should not absorb module-private business rules.
- Shell-level frontend integration should go through the controlled module list in `frontend/packages/shell/src/registerModules.ts`.
- Shared declarations that multiple modules rely on should move through `Kernel / SDK / Contracts`, not be copied into individual modules.
- Migration or import logic should stay in explicit migration/import layers, not leak into host composition.
