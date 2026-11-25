# Vairas.Scheduling — code coverage

**Line coverage 87.05 %** (2347/2696 executable lines), branch coverage 66.24 % — threshold 70 % **met**. Generated 2026-11-25.

| Assembly | Line % | Lines | Branch % |
|---|---:|---:|---:|
| Vairas.Scheduling | 67.86 | 511/753 | 41.72 |
| Vairas.Scheduling.Business | 94.81 | 1041/1098 | 70.97 |
| Vairas.Scheduling.Domain | 94.2 | 276/293 | 77.84 |
| Vairas.Scheduling.Persistance | 94.02 | 519/552 | 90.57 |

## How it was measured

- Tool: coverlet (`XPlat Code Coverage`) driven by `Backend/local/coverage.py`; settings `Vairas.Scheduling/coverage.runsettings` (migrations and test assemblies excluded).
- Test projects: `Vairas.Scheduling.Tests`; line hits of all test projects of the service are merged (maximum per line), so shared code is never counted twice.
- Filter: `TestCategory!=Broker`. Broker tests need the Service Bus emulator and are executed (and passed) separately by `local/test_all.py`.
- The numbers are measured line coverage, not a statement about functional completeness; acceptance status is tracked in `Documentation/Final-Verification/AC-Traceability-Matrix.xlsx`.

## Reproduce

```powershell
cd Backend
py -3 local/coverage.py --services Vairas.Scheduling --filter "TestCategory!=Broker" --threshold 70
```

## Files

`coverage-summary.json` (machine-readable, per file) and `index.html` (per-file table, lowest coverage first) are in this folder.

## Least covered files

| File | Line % | Lines |
|---|---:|---:|
| `Vairas.Scheduling.Business/IOwners.cs` | 0.0 | 0/3 |
| `Vairas.Scheduling.Business/LearningSchedulingReads.cs` | 0.0 | 0/15 |
| `Vairas.Scheduling.Business/NotificationSourceService.cs` | 0.0 | 0/8 |
| `Integrations/OutboxContextFactory.cs` | 0.0 | 0/1 |
| `Integrations/ServiceBusSender.cs` | 0.0 | 0/11 |
| `Controllers/AssistantPreviewsController.cs` | 0.0 | 0/3 |
| `Controllers/ConfirmedLessonsController.cs` | 0.0 | 0/12 |
| `Controllers/LearningIntegrationController.cs` | 0.0 | 0/3 |
