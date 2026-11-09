using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.API.Infrastructure;
using SchoolAdmission.Application;
using SchoolAdmission.Infrastructure;
using SchoolAdmission.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(o =>
    {
        // Malformed JSON, wrong types, bad GUIDs => 400 in the same error shape as everything else.
        o.InvalidModelStateResponseFactory = ctx => new ObjectResult(GlobalExceptionHandler.Problem(
            ctx.HttpContext, StatusCodes.Status400BadRequest, "VALIDATION_FAILED",
            "The request is invalid. Please check the submitted values."))
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    });

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var allowedOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length > 0)
        p.WithOrigins(allowedOrigins).WithHeaders("Content-Type", "X-Student-Id").WithMethods("GET", "POST", "DELETE");
}));

var app = builder.Build();

// Skipped by tests that bring their own database lifecycle.
if (!app.Configuration.GetValue("SkipMigrations", false))
{
    await DatabaseInitializer.MigrateAsync(app.Services, app.Lifetime.ApplicationStopping);
}

app.UseExceptionHandler();
app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
