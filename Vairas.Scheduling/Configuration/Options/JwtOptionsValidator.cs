using Microsoft.Extensions.Options;

namespace Vairas.Scheduling.Configuration.Options;

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        foreach (var value in new[] { options.Authority, options.Issuer })
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return ValidateOptionsResult.Fail("Authentication requires HTTPS.");
        }
        return ValidateOptionsResult.Success;
    }
}
