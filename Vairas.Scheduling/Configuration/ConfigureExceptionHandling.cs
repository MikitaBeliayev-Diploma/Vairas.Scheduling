using Microsoft.AspNetCore.Mvc;
using Vairas.Scheduling.Middleware;

namespace Vairas.Scheduling.Configuration;

public static class ConfigureExceptionHandling
{
    public static IServiceCollection AddExceptionHandling(this IServiceCollection services)
    {
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
            new BadRequestObjectResult(new ProblemDetails
            {
                Status = 400,
                Title = "The request is invalid.",
                Extensions = { { "code", "VALIDATION_FAILED" }, { "correlationId", CorrelationMiddleware.Id(context.HttpContext) } }
            })
            { ContentTypes = { "application/problem+json" } });
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }
}
