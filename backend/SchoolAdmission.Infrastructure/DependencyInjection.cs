using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolAdmission.Application.Abstractions;
using SchoolAdmission.Infrastructure.BackgroundServices;
using SchoolAdmission.Infrastructure.Persistence;

namespace SchoolAdmission.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured (set ConnectionStrings__DefaultConnection).");

        // Burst traffic queues for a pooled connection inside the app (cheap) instead of piling up in
        // PostgreSQL. 50 stays safely below Postgres' default max_connections (100). Explicit values in
        // the configured connection string always win.
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        if (!csb.ContainsKey("Maximum Pool Size")) csb.MaxPoolSize = 50;
        if (!csb.ContainsKey("Timeout")) csb.Timeout = 30;

        services.AddDbContext<AdmissionDbContext>(o => o
            .UseNpgsql(csb.ConnectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAdmissionRepository, AdmissionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Tests that need full control over expiry timing turn the sweeper off.
        if (!configuration.GetValue("DisableExpirationSweeper", false))
            services.AddHostedService<HoldExpirationService>();

        return services;
    }
}
