# NewP

NewP is an early-stage, independently maintained .NET and React platform for modular applications. It brings together a backend host, frontend shell, stable contracts, SDKs, metadata-driven CRUD, authorization, and persistence infrastructure.

The goal is to let application developers reuse common platform mechanisms and add modules through explicit extension points. Modules declare their identity, dependencies, navigation, models, and permission references; behavior implementations bind through stable keys.

[中文说明](README.zh-CN.md) · [Documentation](docs/README.md) · [Module quickstart](docs/developer/componentized-development-quickstart.md)

## Project status

This is an early open-source snapshot, first published on 2026-10-22. Development began privately before this snapshot; the public Git history starts with this source release. There are currently no confirmed external adopters and no stable public release. APIs and authoring formats may evolve. Existing tests and bounded acceptance scenarios cover specific behavior; their presence does not certify every deployment or production workload.

## Prerequisites

- PowerShell 7 on Windows for the documented scripts.
- .NET 10 SDK for the current backend projects.
- Node.js 24.19.0 and pnpm 11.20.0, as declared in `frontend/package.json`.
- A SQL Server instance and a database connection authorized for your local development environment.

## Local development

The committed connection strings are local examples without database passwords. Configure your own development connection in the process environment before starting the host:

```powershell
$env:NEWP_DEV_SQLSERVER_CONNECTION_STRING = Read-Host 'Enter your local SQL Server connection string'
.\scripts\bootstrap.ps1
.\scripts\dev.ps1
```

`Read-Host` avoids placing the connection value in shell command history. It is a convenience for local setup, not a credential store. Keep credentials out of issues, source files, screenshots, and commits. The root `.env` file is not automatically loaded by the development script.

Default endpoints are `http://localhost:5000` for the Host API and `http://localhost:3000` for the frontend. Stop the development services before initializing an administrator for a new database:

```powershell
.\scripts\stop-dev.ps1
$env:ConnectionStrings__SqlServer = $env:NEWP_DEV_SQLSERVER_CONNECTION_STRING
dotnet run --project .\apps\host-api\HostApi.csproj -- identity bootstrap-local-admin
```

See the [local environment guide](docs/guides/local-environment.md) for database, test, and administrator setup. Do not point automated tests at a database containing valuable data.

## Architecture and extension

- `apps/`: application entry points and host composition.
- `server/`: contracts, SDKs, kernel, platform mechanisms, and backend modules.
- `frontend/`: shell, shared UI, frontend SDKs, metadata, and modules.
- `tests/`: unit, integration, and bounded acceptance tests.
- `scripts/`: development and verification entry points.

Start with the [module quickstart](docs/developer/componentized-development-quickstart.md) and [componentization boundaries](docs/architecture/componentization-guardrails.md). Most detailed documentation is currently in Simplified Chinese.

## Verification and contributions

Select the smallest checks that prove your change using the [verification guide](docs/verification/quick-verification.md). There is currently no CI; verification runs locally. Environment-dependent tests may be skipped unless their prerequisites are explicitly enabled.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a change and [SECURITY.md](SECURITY.md) for security reporting.

## License

NewP-authored code is available under the [MIT License](LICENSE). Third-party dependencies retain their own licenses; inspect their notices before redistributing a combined artifact.
