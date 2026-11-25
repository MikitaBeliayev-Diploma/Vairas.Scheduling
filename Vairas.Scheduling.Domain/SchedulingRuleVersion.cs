// EF Core / Npgsql 9+ persistence model generated from Database/Vairas_Scheduling_FINAL.dbml.
// Types in other bounded contexts remain logical IDs only.
namespace Vairas.Scheduling.Domain;

public sealed partial class SchedulingRuleVersion
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public int Revision { get; set; }
    public string SourceKind { get; set; } = null!;
    public Guid? SchoolId { get; set; }
    public string? CategoryCode { get; set; }
    public string RuleCode { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string OverridePolicy { get; set; } = null!;
    public string ScopeKind { get; set; } = null!;
    public Guid? ScopeResourceId { get; set; }
    public string? DecisionReason { get; set; }
    public decimal? NumericLimit { get; set; }
    public string? Unit { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public string SourceReference { get; set; } = null!;
    public string VerificationStatus { get; set; } = null!;
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
}
