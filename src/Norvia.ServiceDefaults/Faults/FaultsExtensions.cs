using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Norvia.ServiceDefaults.Faults;

namespace Microsoft.Extensions.Hosting;

public static class FaultsExtensions
{
    /// <summary>
    /// Gives the application the faults it owns (<see cref="IFaults"/>). Each is off until the presenter switches it on through
    /// the fault endpoints, for a duration after which it expires by itself.
    /// </summary>
    public static TBuilder AddFaults<TBuilder>(this TBuilder builder, params FaultDefinition[] faults) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(faults);

        builder.Services.AddOptions<FaultOptions>().BindConfiguration(FaultOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.DefaultDuration <= options.MaxDuration, "Faults:DefaultDuration must not be longer than Faults:MaxDuration.")
            .ValidateOnStart();
        builder.Services.TryAddSingleton(TimeProvider.System);
        foreach (var fault in faults)
        {
            builder.Services.AddSingleton(fault);
        }

        builder.Services.TryAddSingleton<IFaults, FaultSwitchboard>();
        return builder;
    }
}
