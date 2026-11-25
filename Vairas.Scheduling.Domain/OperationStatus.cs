// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public enum OperationStatus
{
    Pending,
    Committed,
    Rejected,
    ReconciliationPending,
}
