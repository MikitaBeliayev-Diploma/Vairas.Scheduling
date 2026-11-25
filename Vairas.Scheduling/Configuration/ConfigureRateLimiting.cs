using System.Threading.RateLimiting;
using Vairas.Scheduling.Middleware;

namespace Vairas.Scheduling.Configuration;

public static class ConfigureRateLimiting
{
    public static IServiceCollection AddQueryRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(http => Partition(http, perUser: true)),
                PartitionedRateLimiter.Create<HttpContext, string>(http => Partition(http, perUser: false)));
            // One constant response for the per-user and per-school limits; the restricted audit records only code, school and actor pseudonym.
            options.OnRejected = async (context, ct) =>
            {
                AvailabilityAudit.Record(context.HttpContext, "RATE_LIMIT_EXCEEDED", context.HttpContext.Request.RouteValues["schoolId"]);
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.WriteProblemAsync(429, "RATE_LIMIT_EXCEEDED", "Query limit exceeded.", ct);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext http, bool perUser)
    {
        // Endpoint metadata and a canonical school GUID: path case, a trailing slash or another GUID spelling cannot open a new counter.
        var (key, permits) = AvailabilityAudit.Partition(http, perUser);
        if (key is null)
            return RateLimitPartition.GetNoLimiter("other");
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new()
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
