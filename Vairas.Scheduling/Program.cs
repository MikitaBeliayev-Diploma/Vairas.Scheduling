using Vairas.Scheduling.Configuration;
using Vairas.Scheduling.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonLogging();
builder.Services
    .AddSchedulingOptions(builder.Configuration)
    .AddStrictJsonSerialization()
    .AddExceptionHandling()
    .AddOpenApiDocument()
    .AddSchedulingDatabase()
    .AddSchedulingServices()
    .AddDependencyHttpClients()
    .AddMessageBroker()
    .AddBearerAuthentication()
    .AddEntryAuthorization()
    .AddServiceHealthChecks()
    .AddQueryRateLimiting()
    .AddServiceTelemetry(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages(context => context.HttpContext.WriteProblemAsync(context.HttpContext.Response.StatusCode, "HTTP_ERROR", "The request could not be completed."));
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.Use((http, next) => Vairas.Delegation.ReadPreviewDelegation.EnforceAsync(http, next, "scheduling"));
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapServiceHealthChecks();
app.MapOpenApiDocument();
app.Run();

public partial class Program;
