using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>Validated UserDeactivated envelope; the fingerprint covers the exact delivered body.</summary>
public sealed record UserDeactivatedEvent(Guid EventId, Guid UserId, long UserRevision, DateTimeOffset OccurredAt, string Fingerprint);
