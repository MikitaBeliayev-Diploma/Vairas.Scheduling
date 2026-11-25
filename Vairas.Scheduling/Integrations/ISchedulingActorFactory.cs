using Microsoft.Extensions.Options;
using Vairas.Scheduling.Configuration.Options;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Vairas.Scheduling.Business;
namespace Vairas.Scheduling.Integrations;

public interface ISchedulingActorFactory {Task<SchedulingActor> CreateAsync(HttpContext http,CancellationToken ct);}
