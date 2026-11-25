using Microsoft.Extensions.Options;

namespace Vairas.Scheduling.Configuration.Options;

/// <summary>A disabled inbox requires no broker or reservation authority credentials.</summary>
public sealed class InboxOptionsValidator(IOptions<DatabaseOptions> database) : IValidateOptions<InboxOptions>
{
    public ValidateOptionsResult Validate(string? name, InboxOptions options) =>
        options.Enabled && (string.IsNullOrWhiteSpace(options.ConnectionString) || string.IsNullOrWhiteSpace(database.Value.ReservationAuthority))
            ? ValidateOptionsResult.Fail("Enabled inbox requires broker credentials and the cross-school reservation authority connection.")
            : ValidateOptionsResult.Success;
}
