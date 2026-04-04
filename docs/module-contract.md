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

