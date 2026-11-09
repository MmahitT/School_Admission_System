using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SchoolAdmission.Infrastructure.Configuration;

/// <summary>
/// Fail-fast check of the configuration the backend needs, run before any service is built. Returns every problem
/// found so an operator can fix them in one go. Messages name the missing setting only; they never echo a value,
/// so a password can never leak through a startup error.
/// </summary>
public static class EnvironmentValidator
{
    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        var problems = new List<string>();

        ValidateConnectionString(configuration, problems);
        ValidatePositiveInt(configuration, "HOLD_DURATION_SECONDS", problems);
        ValidatePositiveInt(configuration, "EXPIRATION_SWEEP_INTERVAL_SECONDS", problems);
        ValidateCorsOrigins(configuration, problems);

        return problems;
    }

    /// <summary>Formats problems for the console. Contains setting names only.</summary>
    public static string Format(IEnumerable<string> problems) =>
        "Invalid configuration. The backend will not start until this is fixed:" + Environment.NewLine +
        string.Join(Environment.NewLine, problems.Select(p => " - " + p)) + Environment.NewLine +
        "See the README, section \"Configuration\" (copy .env.example to .env and set the required values).";

    private static void ValidateConnectionString(IConfiguration configuration, List<string> problems)
    {
        const string name = "ConnectionStrings__DefaultConnection";
        var raw = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(raw))
        {
            problems.Add($"{name} is not set.");
            return;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(raw);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Deliberately not including ex.Message: it can quote part of the connection string.
            problems.Add($"{name} is not a valid PostgreSQL connection string (check for stray ';', '=' or quotes in values).");
            return;
        }

        if (string.IsNullOrWhiteSpace(builder.Host)) problems.Add($"{name} has no Host.");
        if (string.IsNullOrWhiteSpace(builder.Database)) problems.Add($"{name} has no Database (POSTGRES_DB).");
        if (string.IsNullOrWhiteSpace(builder.Username)) problems.Add($"{name} has no Username (POSTGRES_USER).");
        if (string.IsNullOrEmpty(builder.Password)) problems.Add($"{name} has no Password (POSTGRES_PASSWORD).");
    }

    private static void ValidatePositiveInt(IConfiguration configuration, string key, List<string> problems)
    {
        var raw = configuration[key];
        if (raw is null) return; // optional: the application default applies
        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
            problems.Add($"{key} must be a positive whole number.");
    }

    private static void ValidateCorsOrigins(IConfiguration configuration, List<string> problems)
    {
        var raw = configuration["CORS_ALLOWED_ORIGINS"];
        if (string.IsNullOrWhiteSpace(raw)) return; // no cross-origin access is allowed

        foreach (var origin in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ok = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                     && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query);
            if (!ok)
                problems.Add("CORS_ALLOWED_ORIGINS must be a comma-separated list of origins like http://localhost:5173 (no wildcard, no path).");
        }
    }
}
