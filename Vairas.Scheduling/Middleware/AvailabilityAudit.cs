using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Routing;
namespace Vairas.Scheduling.Middleware;

/// <summary>
/// Scoped availability limits (AC-091) and their minimal restricted audit. The audit event carries only the limit code,
/// the normalized requesting school, a non-reversible actor pseudonym and the correlation scope. It never records the
/// learner, enrollment, teacher, vehicle, query window, result or any foreign-school data.
/// </summary>
public static class AvailabilityAudit
{
    public const string Category = "Vairas.Scheduling.Audit";
    public const string RouteName = "findAvailableLessons";
    public static readonly EventId LimitExceeded = new(9101, "AvailabilityLimitExceeded");
    public static readonly IReadOnlySet<string> QueryLimitCodes = new HashSet<string>(StringComparer.Ordinal) { "QUERY_WINDOW_EXCEEDED", "QUERY_VOLUME_EXCEEDED", "RESULT_LIMIT_EXCEEDED" };
    public const int PermitsPerUserAndSchool = 60, PermitsPerSchool = 300;

    /// <summary>Identifies the endpoint from routing metadata, so path case or a trailing slash cannot bypass limits.</summary>
    public static bool IsAvailability(Endpoint? endpoint) => endpoint?.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName == RouteName;

    /// <summary>Canonical school partition: every accepted GUID spelling of one school shares one counter.</summary>
    public static string School(object? routeValue) => Guid.TryParse(routeValue?.ToString(), out var school) ? school.ToString("D") : "invalid";

    /// <summary>Truncated SHA-256 of the authenticated subject: correlates repeated probing without storing the identity itself.</summary>
    public static string Actor(HttpContext http)
    {
        var subject = http.User.FindFirst("sub")?.Value;
        return subject is null ? "anonymous" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("vairas-scheduling-audit/1:" + subject)))[..16];
    }

    /// <summary>Writes the minimal audit event; <paramref name="schoolRouteValue"/> is captured before the exception handler clears routing state.</summary>
    public static void Record(HttpContext http, string code, object? schoolRouteValue)
    {
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        logger.LogWarning(LimitExceeded, "Availability limit {Code} for school {SchoolId} by actor {ActorPseudonym}", code, School(schoolRouteValue), Actor(http));
    }

    public static RateLimitPartitionKey Partition(HttpContext http, bool perUser) =>
        !IsAvailability(http.GetEndpoint()) || http.User.Identity?.IsAuthenticated != true ? new(null, 0)
            : new((perUser ? Actor(http) + ":" : "") + School(http.Request.RouteValues["schoolId"]), perUser ? PermitsPerUserAndSchool : PermitsPerSchool);
}
