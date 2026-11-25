using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Vehicle evidence must be protected by the same Scheduling gate protocol.</summary>
public sealed record VehicleSnapshot(Guid VehicleId, Guid SchoolId, Guid TeacherId, Guid BranchId, string Category, long Revision, bool Eligible, bool GateSupported, DateTimeOffset ValidUntil);
