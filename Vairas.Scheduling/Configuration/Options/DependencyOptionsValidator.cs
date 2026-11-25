using Microsoft.Extensions.Options;

namespace Vairas.Scheduling.Configuration.Options;

public sealed class DependencyOptionsValidator : IValidateOptions<DependencyOptions>
{
    public ValidateOptionsResult Validate(string? name, DependencyOptions options)
    {
        foreach (var origin in new[] { options.AuthBaseUrl, options.UserManagementBaseUrl, options.SchoolBaseUrl })
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                return ValidateOptionsResult.Fail("Dependency origins require HTTPS without embedded credentials.");
        }
        return ValidateOptionsResult.Success;
    }
}
