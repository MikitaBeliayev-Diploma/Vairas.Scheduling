using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Routing;
namespace Vairas.Scheduling.Middleware;

/// <summary>Null key means unlimited.</summary>
public readonly record struct RateLimitPartitionKey(string? Key, int Permits);
