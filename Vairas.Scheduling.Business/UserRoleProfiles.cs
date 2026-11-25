using Vairas.Scheduling.Domain;
namespace Vairas.Scheduling.Business;

/// <summary>User Management's role binding for one global user (GET /internal/v1/users/{userId}/role-profiles).</summary>
public sealed record UserRoleProfiles(Guid UserId, string Status, long Revision, Guid? StudentId, Guid? TeacherId, Guid? AdministratorId);
