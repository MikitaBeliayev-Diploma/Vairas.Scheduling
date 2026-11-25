using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vairas.Scheduling.Domain;
using Vairas.Scheduling.Domain.Rules;
namespace Vairas.Scheduling.Business;

/// <summary>Factories for the common application errors; each returns the category exception for the intended HTTP semantics.</summary>
public static class SchedulingErrors
{
    public static BadRequestException Invalid(string code = "VALIDATION_FAILED") => new(code);
    public static NotFoundException Missing() => new("NOT_FOUND");
    public static ForbiddenException Denied() => new("ACCESS_DENIED");
    public static ServiceUnavailableException Dependency(string code = "DEPENDENCY_UNAVAILABLE") => new(code);
    public static ConflictException Conflict(string code = "REVISION_CONFLICT") => new(code);

    /// <summary>The status stored with an idempotent operation outcome so that a replay reproduces the same category.</summary>
    public static int OutcomeStatus(BusinessException exception) => exception switch
    {
        BadRequestException => 400, UnauthorizedException => 401, ForbiddenException => 403, NotFoundException => 404,
        ConflictException => 409, UnprocessableEntityException => 422, ServiceUnavailableException => 503, OperationPendingException => 202,
        _ => throw new InvalidOperationException("Unsupported application exception category.")
    };

    /// <summary>Rebuilds the category from a persisted outcome status (idempotent replay of a stored decision).</summary>
    public static BusinessException FromStatus(int status, string code) => status switch
    {
        400 => new BadRequestException(code), 401 => new UnauthorizedException(code), 403 => new ForbiddenException(code),
        404 => new NotFoundException(code), 409 => new ConflictException(code), 422 => new UnprocessableEntityException(code),
        503 => new ServiceUnavailableException(code), 202 => new OperationPendingException(code),
        _ => throw new InvalidOperationException("A persisted outcome carries an unsupported status.")
    };
}
