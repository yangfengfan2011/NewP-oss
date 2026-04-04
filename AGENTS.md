# AGENTS.md

## Project identity
This repository is a platform foundation and extension kernel, not a business application.
The platform provides reusable host, contracts, SDKs, and built-in public modules for product and project teams.

## Primary goals
- Keep module boundaries explicit.
- Prefer extension contracts over direct coupling.
- Separate platform kernel from product logic.
- Treat migration as code + metadata + data + hosts + tooling.
- Keep changes verifiable and reversible.

## Architecture rules
- Host is responsible for startup, composition, auth integration, route/menu aggregation, and module loading only.
- Kernel provides shared abstractions and infrastructure capabilities.
- Built-in modules depend on contracts and shared kernel abstractions, not on each other's concrete infrastructure.
- New extensibility must go through module contracts and SDKs.
- Do not create cross-module direct database access.
- Do not put product-specific screens or workflows into the platform shell.
- Do not let migration shortcuts become long-term platform architecture.

## Migration rules
- Inventory before implementation.
- Every old asset must be classified as one of: Retire, Rewrite, Wrap, Import, Postpone.
- Do not migrate old runtime metadata implicitly; document its target form first.
- Do not copy old framework bootstrap code directly into the new host without a contract review.
- Keep an explicit compatibility list for any temporary adapters.
- Treat WinApp, Studio, and file-service rewrites as out of scope unless the current phase explicitly includes them.

## Backend rules
- Use the current LTS .NET target for the new platform.
- Use dependency injection for all service composition.
- Keep controllers thin; business logic belongs to application/domain services.
- Expose stable DTO contracts.
- Add unit/integration tests for every new module capability.

## Frontend rules
- Use React + TypeScript + Vite.
- Shell handles layout, auth context, menu composition, route composition, and global providers.
- Shared components belong in `frontend/packages/ui`.
- Module UIs must register through the frontend SDK, not by editing shell internals directly.
- Avoid global mutable state unless clearly justified.
- Any old page or metadata compatibility path must be isolated and documented.

## Change rules
- One task should focus on one module or one capability only.
- Do not mix refactor, rename, and new feature work in a single change unless required.
- Read relevant files first, then propose a plan, then edit.
- Keep diffs small when possible.
- Update docs when contracts, architecture, or migration decisions change.

## Forbidden changes without explicit request
- Do not introduce microservices.
- Do not casually modify deployment scripts, secrets handling, CI pipelines, or infrastructure provisioning.
- Do not replace foundational libraries without documenting tradeoffs.
- Do not silently discard old metadata/config assets because they look obsolete.

## Validation
Every change must try to run:
1. backend build
2. backend tests
3. frontend build
4. frontend tests
5. contract or smoke checks when relevant

## Output expectations
Always report:
- what changed
- affected files/modules
- validation results
- known risks
- rollback approach
