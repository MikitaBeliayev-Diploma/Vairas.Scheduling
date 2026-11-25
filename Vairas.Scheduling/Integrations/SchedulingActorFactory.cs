using Microsoft.Extensions.Options;
using Vairas.Scheduling.Configuration.Options;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Vairas.Scheduling.Business;
namespace Vairas.Scheduling.Integrations;

public sealed class SchedulingActorFactory(IDependencyTransport transport,IOptions<DependencyOptions> settings,TimeProvider clock) : ISchedulingActorFactory
{
    private readonly DependencyOptions options = settings.Value;
    public async Task<SchedulingActor> CreateAsync(HttpContext http,CancellationToken ct)
    {
        var auth=await http.AuthenticateAsync("User");
        if(!auth.Succeeded || auth.Principal is null || !Guid.TryParse(auth.Principal.FindFirstValue("sub"),out var identity) || identity==Guid.Empty || !Guid.TryParse(auth.Principal.FindFirstValue("sid"),out var sid) || sid==Guid.Empty)throw new UnauthorizedException("INVALID_IDENTITY");
        var start=clock.GetUtcNow();
        var session=await transport.SendAsync<UserSession>(options.AuthBaseUrl,"internal/v1/sessions/validate",new {identityId=identity,sessionId=sid},null,ct);
        if(session.IdentityId!=identity || session.SessionId!=sid || session.ValidUntil<=clock.GetUtcNow())throw SchedulingErrors.Dependency("INVALID_SESSION_EVIDENCE");
        if(!session.Active)throw new UnauthorizedException("SESSION_INVALID");
        var delegated=http.Request.Path.StartsWithSegments("/internal/v1/delegated");
        var header=(delegated?http.Request.Headers["X-User-Authorization"]:http.Request.Headers.Authorization).ToString();
        if(!header.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedException("AUTHENTICATION_REQUIRED");
        var profile=Vairas.Delegation.ReadPreviewDelegation.Marked(auth.Principal)
            ? await transport.SendAsync<Profile>(options.UserManagementBaseUrl,"internal/v1/profiles/assistant-actor",new {identityId=identity,sessionId=sid},null,ct)
            : await transport.SendAsync<Profile>(options.UserManagementBaseUrl,"api/v1/users/me",null,header[7..],ct);
        if(profile.UserId==Guid.Empty || profile.Status!="Active")throw SchedulingErrors.Denied();
        var until=session.ValidUntil<start.AddSeconds(30)?session.ValidUntil:start.AddSeconds(30);
        var execution=Vairas.ConfirmedActions.LessonActions.IsExecution(auth.Principal);
        var executionBinding=execution?Vairas.ConfirmedActions.LessonActions.Parse(auth.Principal,2):null;
        if(executionBinding is not null && executionBinding.UserId!=profile.UserId)throw SchedulingErrors.Denied();
        if(executionBinding is not null && long.TryParse(auth.Principal.FindFirstValue("exp"),out var tokenExpiry)){var expires=DateTimeOffset.FromUnixTimeSeconds(tokenExpiry);if(expires<until)until=expires;}
        var workload=delegated?(await http.AuthenticateAsync("Workload")).Principal?.FindFirstValue("sub"):null;
        Guid? confirmation=Guid.TryParse(http.Request.Headers["X-Confirmation-ID"],out var confirm)?confirm:null;
        return new(identity,profile.UserId,profile.StudentId,profile.TeacherId,delegated || execution?"MCP":"Web",execution?"mcp-server":workload,until,executionBinding?.PreviewId??confirmation);
    }
    private sealed record UserSession(bool Active,Guid IdentityId,Guid SessionId,DateTimeOffset ValidUntil);
    private sealed record Profile(Guid UserId,string Status,Guid? StudentId,Guid? TeacherId,Guid? AdministratorId);
}
