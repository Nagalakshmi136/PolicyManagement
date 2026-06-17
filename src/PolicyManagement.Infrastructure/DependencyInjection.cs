using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolicyManagement.Domain.Repositories;
using PolicyManagement.Infrastructure.Persistence;
using PolicyManagement.Infrastructure.Persistence.Interceptors;
using PolicyManagement.Infrastructure.Persistence.Repositories;
using PolicyManagement.Infrastructure.Persistence.Seed;

namespace PolicyManagement.Infrastructure;

/// <summary>
/// Infrastructure DI registration extension.
/// Called once from <c>Program.cs</c> — the only place the API project
/// references Infrastructure types directly.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var auditInterceptor = new AuditSaveChangesInterceptor();

        services.AddDbContext<AppDbContext>(options =>
            options
                .UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    sql => sql
                        .MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                        .EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorNumbersToAdd: null))
                .AddInterceptors(auditInterceptor));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();

        return services;
    }

    /// <summary>
    /// Registers the EF Core health check for the readiness probe.
    /// Call from <c>Program.cs</c> after <see cref="AddInfrastructure"/>:
    /// <code>builder.Services.AddInfrastructureHealthChecks();</code>
    /// Then map with the <c>"ready"</c> tag on <c>/health/ready</c>.
    /// </summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(
        this IHealthChecksBuilder builder)
    {
        builder.AddDbContextCheck<AppDbContext>(tags: ["ready"]);
        return builder;
    }

    /// <summary>
    /// Applies pending EF Core migrations and seeds reference data.
    /// Called at application startup in Development and Integration environments only.
    /// <c>Program.cs</c> has no direct knowledge of <see cref="AppDbContext"/> or
    /// <see cref="DataSeeder"/>; all Infrastructure-concrete types stay in this layer.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(IServiceProvider serviceProvider)
    {
        using var scope  = serviceProvider.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        await db.Database.MigrateAsync();
        await DataSeeder.SeedAsync(db, logger);
    }
}
