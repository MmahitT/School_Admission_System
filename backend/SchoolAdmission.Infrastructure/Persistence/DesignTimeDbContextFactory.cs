using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SchoolAdmission.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` tooling. Reads the same env var as the app; no credentials in source.
/// Creating migrations never connects to a database, so the fallback needs no username or password.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AdmissionDbContext>
{
    public AdmissionDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                 ?? "Host=localhost;Database=design_time_only";
        var options = new DbContextOptionsBuilder<AdmissionDbContext>()
            .UseNpgsql(cs)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AdmissionDbContext(options);
    }
}
