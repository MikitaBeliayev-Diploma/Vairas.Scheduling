using Microsoft.Extensions.Options;
using Vairas.Scheduling.Business;

namespace Vairas.Scheduling.Configuration.Options;

public sealed class SchedulingOptionsValidator(IHostEnvironment environment) : IValidateOptions<SchedulingOptions>
{
    public ValidateOptionsResult Validate(string? name, SchedulingOptions options)
    {
        if (options.SyntheticMode && environment.IsProduction())
            return ValidateOptionsResult.Fail("SyntheticMode is restricted to isolated Development/Testing environments.");
        if (options.MaximumPlanningDays is < 1 or > 366 || options.MaximumLessons is < 1 or > 1000
            || options.MaximumSearchNodes < 1 || options.GenerationSeconds is < 1 or > 30)
            return ValidateOptionsResult.Fail("Invalid scheduling resource limits.");
        return ValidateOptionsResult.Success;
    }
}
