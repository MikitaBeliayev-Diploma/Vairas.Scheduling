using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Incomplete editable draft; a validated submission freezes it.</summary>
public sealed record DraftInputs(Guid SchoolId, Guid? ProgramVersionId = null, Guid[]? EnrollmentIds = null, Guid[]? GroupIds = null,
    Guid[]? TeacherIds = null, Guid[]? VehicleIds = null, DateOnly? PlanningStart = null, DateOnly? PlanningEnd = null, Guid[]? RuleVersionIds = null,
    TimeWindow[]? Windows = null, string[]? Preferences = null, int? ExpectedRevision = null, RecurrenceRequest[]? Recurrences = null);
