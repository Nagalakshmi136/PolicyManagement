using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PolicyManagement.Application.Common.Behaviours;

namespace PolicyManagement.Application;

/// <summary>
/// DI registration extension for the Application layer.
/// Call <c>builder.Services.AddApplication()</c> from <c>Program.cs</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehaviour<,>));     // outermost — measures total time
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));  // inner — throws on invalid input
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
