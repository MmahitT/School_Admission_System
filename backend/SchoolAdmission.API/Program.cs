using SchoolAdmission.Infrastructure;
using SchoolAdmission.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Skipped by tests that bring their own database lifecycle.
if (!app.Configuration.GetValue("SkipMigrations", false))
{
    await DatabaseInitializer.MigrateAsync(app.Services, app.Lifetime.ApplicationStopping);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
