namespace Vairas.Scheduling.Configuration.Options;

/// <summary>Bound from "Authentication": token validation settings for user and workload callers.</summary>
public sealed class JwtOptions
{
    public string Authority { get; set; } = "https://localhost:7100";
    public string Issuer { get; set; } = "https://localhost:7100";
    public string UserAudience { get; set; } = "vairas-scheduling";
    public string WorkloadAudience { get; set; } = "vairas-scheduling-internal";
    // Local allowlist intersects with signed scopes; absent configuration grants nothing.
    public Dictionary<string, string[]> WorkloadClients { get; set; } = [];
}
