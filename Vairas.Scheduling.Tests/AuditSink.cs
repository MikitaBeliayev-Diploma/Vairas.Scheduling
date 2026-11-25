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

/// <summary>Restricted audit capture: only the audit category is retained.</summary>
internal sealed class AuditSink : ILoggerProvider
{
    public sealed record Entry(EventId Event, LogLevel Level, string Message, IReadOnlyDictionary<string, string?> Fields);
    public readonly ConcurrentQueue<Entry> Entries = new();
    public ILogger CreateLogger(string category) => new Sink(this, category);
    public void Dispose() { }
    private sealed class Sink(AuditSink owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            if (category != AvailabilityAudit.Category) return;
            var fields = (state as IEnumerable<KeyValuePair<string, object?>> ?? []).ToDictionary(x => x.Key, x => x.Value?.ToString());
            owner.Entries.Enqueue(new(id, level, format(state, error), fields));
        }
    }
}
