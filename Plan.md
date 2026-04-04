# Plan.md

## Phase 0: Inventory and disposition
- Scan old hosts, projects, runtime metadata, config assets, and external dependencies
- Produce asset inventory
- Produce disposition matrix: Retire / Rewrite / Wrap / Import / Postpone
- Freeze first-wave scope

Validation:
- inventory document exists
- disposition matrix exists
- first-wave module list is approved

## Phase 1: Repository foundation
- Create backend host, kernel, contracts, infrastructure skeleton
- Create frontend shell, ui, core, sdk skeleton
- Create minimum runnable startup path
- Add docs, scripts, AGENTS.md, Codex config

Validation:
- backend build
- frontend install
- frontend build
- backend can start
- frontend can start

## Phase 2: Minimum runnable platform
- Implement Identity Lite / LocalAuth
- Add login page
- Add login API, current-user API, logout API
- Seed default admin or local admin account
- Connect auth context, route guard, and shell
- Wire demo menu and one protected demo page
- Produce minimum smoke validation steps

Validation:
- can open login page
- can log in locally
- can enter shell after login
- can fetch current user
- can access one protected demo page after login
- cannot access protected page after logout

## Phase 3: Extension contracts
- Define server module registration contract
- Define menu/route/permission/config contracts
- Define migration/import contract
- Build demo module on backend and frontend

Validation:
- contract tests
- shell can load demo module
- permission and route registration visible

## Phase 4: Core built-in modules
- Authorization
- ConfigCenter
- Audit

Validation:
- backend tests
- frontend build
- smoke tests

## Phase 5: Migration runway
- Define metadata migration strategy
- Deliver one importer or compatibility slice
- Migrate one real old asset class end-to-end

Validation:
- import/compatibility path documented
- one old asset class verified in new platform

## Phase 6: Developer experience
- module templates
- SDK helpers
- test templates
- docs and examples

Validation:
- scaffold a sample module end-to-end

## Default postponed scope
- WinApp rewrite
- Studio rewrite
- file-service rewrite and deep migration
- workflow and report redesign
