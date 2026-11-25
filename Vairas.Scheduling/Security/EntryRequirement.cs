using Microsoft.AspNetCore.Authorization;

namespace Vairas.Scheduling.Security;

public sealed record EntryRequirement(string? Scope = null) : IAuthorizationRequirement;
