extern alias SchedulingApi;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using SchedulingProgram = SchedulingApi::Program;
using AvailabilityAudit = SchedulingApi::Vairas.Scheduling.Middleware.AvailabilityAudit;
using ISchedulingActorFactory = SchedulingApi::Vairas.Scheduling.Integrations.ISchedulingActorFactory;
namespace Vairas.Scheduling.Tests;

/// <summary>The real Scheduling HTTP pipeline (routing, JWT validation, authorization, limiter, ProblemDetails) over in-memory owners.</summary>
internal sealed class SideChannelHost : WebApplicationFactory<SchedulingProgram>
{
    public const string Issuer = "https://localhost:7100";
    private readonly RsaSecurityKey key = new(RSA.Create(2048)) { KeyId = "side-channel-test" };
    public TestWorld World { get; set; } = new();
    public readonly AuditSink Audit = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); builder.UseContentRoot(Path.Combine(PostgresFixture.Root, "Vairas.Scheduling"));
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Scheduling"] = "Host=unused.invalid", ["Scheduling:SyntheticMode"] = "true" }));
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ISchedulingService>(_ => World.Service); services.AddScoped<ISchedulingPreviewService>(_ => World.Service);
            services.AddScoped<ISchedulingActorFactory>(_ => new ClaimActors(this));
            services.AddSingleton<ILoggerProvider>(Audit);
            services.PostConfigure<JwtBearerOptions>("User", o =>
            {
                var config = new OpenIdConnectConfiguration { Issuer = Issuer }; config.SigningKeys.Add(key);
                o.Configuration = config; o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
            });
        });
    }
    public string Token(Guid subject) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer, Audience = "vairas-scheduling", Expires = DateTime.UtcNow.AddHours(1),
        Claims = new Dictionary<string, object> { ["sub"] = subject.ToString() }, SigningCredentials = new(key, SecurityAlgorithms.RsaSha256)
    });
    /// <summary>Synthetic actor: the authenticated subject becomes the acting user of the current world.</summary>
    private sealed class ClaimActors(SideChannelHost host) : ISchedulingActorFactory
    {
        public Task<SchedulingActor> CreateAsync(HttpContext http, CancellationToken ct)
        {
            var user = Guid.Parse(http.User.FindFirst("sub")!.Value);
            return Task.FromResult(host.World.Actor with { IdentityId = user, UserId = user });
        }
    }
}
