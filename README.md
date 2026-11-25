# Vairas Scheduling

School-scoped lesson planning and booking service (ASP.NET Core 10, PostgreSQL, EF Core/Npgsql). It owns practical and
theory lesson allocation, cross-school resource conflict prevention (PostgreSQL exclusion constraints), the physical
lesson lifecycle, vehicle booking gates and the lesson outbox/inbox. Learning Tracking owns attendance and credit;
Vehicle Management owns operational eligibility.

## Projects

| Project | Responsibility |
|---|---|
| `Vairas.Scheduling` | HTTP API: controllers, `Configure*` registration, security, integrations, workers |
| `Vairas.Scheduling.Business` | Application services and ports |
| `Vairas.Scheduling.Domain` | Rule engine and entities (no ASP.NET/EF dependencies) |
| `Vairas.Scheduling.Persistance` | `SchedulingDbContext`, migrations, outbox store, reservation authority |
| `Vairas.Scheduling.Tests` | Unit, PostgreSQL and four-service tests |

## Dependencies

PostgreSQL 17, Authentication (tokens), User Management, School Management; optionally Vehicle, Learning, Chat, and
a Service Bus broker for the outbox/inbox. Origin `https://localhost:7300`.

## Essential configuration

Options are bound and validated at startup (`Configuration/ConfigureOptions.cs`). Use `__` in environment variables.

| Section | Required |
|---|---|
| `ConnectionStrings:Scheduling` | always (restricted runtime role) |
| `ConnectionStrings:Outbox` / `ConnectionStrings:ReservationAuthority` | only when `Outbox:Enabled` / `Inbox:Enabled` |
| `Authentication` | HTTPS `Authority` and `Issuer`, audiences, `WorkloadClients` allowlist |
| `Dependencies` | HTTPS `AuthBaseUrl`, `UserManagementBaseUrl`, `SchoolBaseUrl`; `ClientId`/`ClientSecret`/`Scope` |
| `Scheduling` | limits; `SyntheticMode` is rejected in Production |
| `Outbox`, `Inbox` | broker `ConnectionString`, topics; disabled integrations need no credentials |

## Run locally

```powershell
dotnet run --project Vairas.Scheduling --launch-profile https
```

The full local stack (databases, roles, broker emulator) is started from `../local/Start.ps1`.
Database bootstrap and grants: `database/*.sql`; EF migrations are applied by the migration role, never on startup.

## Tests

```powershell
dotnet test Vairas.Scheduling.slnx          # PostgreSQL tests use Testcontainers (Docker required)
./scripts/verify.ps1 -SkipIntegration       # unit tests with the coverage gate
```

Contract notes: [CONTRACT-NOTES.md](CONTRACT-NOTES.md). API exports: `api/`. Extended notes, validation reports and
evidence: `../../Documentation/Backend/Vairas.Scheduling/`.

## Tests and coverage

Requirements: .NET SDK 10 and Docker (PostgreSQL integration tests start a throw-away container via Testcontainers).

```powershell
dotnet test Vairas.Scheduling.slnx                  # unit + integration tests
dotnet test Vairas.Scheduling.slnx --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults   # measure coverage
```

**Coverage report:** [docs/coverage/README.md](docs/coverage/README.md) (summary, per-assembly table, least covered files); machine-readable `docs/coverage/coverage-summary.json` and `docs/coverage/index.html` (per-file table). The raw cobertura XML of a local run is written to `TestResults/`.