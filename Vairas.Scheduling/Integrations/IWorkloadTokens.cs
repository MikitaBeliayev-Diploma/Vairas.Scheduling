using Microsoft.Extensions.Options;
using Vairas.Scheduling.Configuration.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Middleware;
namespace Vairas.Scheduling.Integrations;

public interface IWorkloadTokens
{
    Task<string> GetAsync(CancellationToken ct);
}
