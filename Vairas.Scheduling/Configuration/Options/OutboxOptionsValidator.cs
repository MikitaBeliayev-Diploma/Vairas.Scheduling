using Microsoft.Extensions.Options;

namespace Vairas.Scheduling.Configuration.Options;

/// <summary>A disabled outbox requires no broker or worker credentials.</summary>
public sealed class OutboxOptionsValidator(IOptions<DatabaseOptions> database) : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options) =>
        options.Enabled && (string.IsNullOrWhiteSpace(options.ConnectionString) || string.IsNullOrWhiteSpace(database.Value.Outbox))
            ? ValidateOptionsResult.Fail("Enabled outbox requires broker and separate database worker connections.")
            : ValidateOptionsResult.Success;
}
