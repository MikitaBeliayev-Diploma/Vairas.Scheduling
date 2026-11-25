using Microsoft.Extensions.Options;

namespace Vairas.Scheduling.Configuration.Options;

public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options) =>
        string.IsNullOrWhiteSpace(options.Scheduling)
            ? ValidateOptionsResult.Fail("ConnectionStrings:Scheduling is required.")
            : ValidateOptionsResult.Success;
}
