using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SchoolAdmission.Infrastructure.Configuration;
using Xunit;

namespace SchoolAdmission.Tests.Unit;

public class EnvironmentValidatorTests
{
    private const string Good = "Host=db;Port=5432;Database=school;Username=app;Password=Zx81kQ0pLm";

    private static IReadOnlyList<string> Validate(params (string Key, string? Value)[] settings) =>
        EnvironmentValidator.Validate(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    [Fact]
    public void A_complete_configuration_is_valid()
    {
        Validate(("ConnectionStrings:DefaultConnection", Good), ("HOLD_DURATION_SECONDS", "600"),
                 ("EXPIRATION_SWEEP_INTERVAL_SECONDS", "5"), ("CORS_ALLOWED_ORIGINS", "http://localhost:5173, https://school.example"))
            .Should().BeEmpty();
    }

    [Fact]
    public void Optional_settings_may_be_absent()
    {
        Validate(("ConnectionStrings:DefaultConnection", Good)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_connection_string_is_reported(string? value)
    {
        Validate(("ConnectionStrings:DefaultConnection", value)).Should().ContainSingle().Which.Should().Contain("ConnectionStrings__DefaultConnection is not set");
    }

    [Fact]
    public void A_missing_connection_string_key_is_reported()
    {
        Validate().Should().ContainSingle().Which.Should().Contain("is not set");
    }

    [Theory]
    [InlineData("Host=db;Database=school;Username=app;Password=", "Password")]
    [InlineData("Host=db;Database=school;Username=app", "Password")]
    [InlineData("Host=db;Database=school;Password=Zx81kQ0pLm", "Username")]
    [InlineData("Host=db;Username=app;Password=Zx81kQ0pLm", "Database")]
    [InlineData("Database=school;Username=app;Password=Zx81kQ0pLm", "Host")]
    public void Each_missing_part_of_the_connection_string_is_named(string connection, string missing)
    {
        Validate(("ConnectionStrings:DefaultConnection", connection)).Should().ContainSingle().Which.Should().Contain($"has no {missing}");
    }

    [Fact]
    public void Several_problems_are_reported_together()
    {
        Validate(("ConnectionStrings:DefaultConnection", "Host=db"), ("HOLD_DURATION_SECONDS", "-5"))
            .Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("ten")]
    [InlineData("1.5")]
    [InlineData("")]
    public void Durations_must_be_positive_whole_numbers(string value)
    {
        Validate(("ConnectionStrings:DefaultConnection", Good), ("HOLD_DURATION_SECONDS", value))
            .Should().ContainSingle().Which.Should().Contain("HOLD_DURATION_SECONDS");
        Validate(("ConnectionStrings:DefaultConnection", Good), ("EXPIRATION_SWEEP_INTERVAL_SECONDS", value))
            .Should().ContainSingle().Which.Should().Contain("EXPIRATION_SWEEP_INTERVAL_SECONDS");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("localhost:5173")]
    [InlineData("http://localhost:5173/app")]
    [InlineData("ftp://example.com")]
    public void Cors_origins_must_be_explicit_http_origins(string origins)
    {
        Validate(("ConnectionStrings:DefaultConnection", Good), ("CORS_ALLOWED_ORIGINS", origins))
            .Should().ContainSingle().Which.Should().Contain("CORS_ALLOWED_ORIGINS");
    }

    [Fact]
    public void Error_messages_never_contain_the_password_or_any_connection_string_value()
    {
        const string secret = "Sup3rS3cretValue99";
        var malformed = Validate(("ConnectionStrings:DefaultConnection", $"Host=db;Password={secret};;=oops;\"unterminated"));
        var missingHost = Validate(("ConnectionStrings:DefaultConnection", $"Database=x;Username=u;Password={secret}"));
        var invalidDuration = Validate(("ConnectionStrings:DefaultConnection", $"Host=h;Database=x;Username=u;Password={secret}"), ("HOLD_DURATION_SECONDS", secret));

        foreach (var problems in new[] { malformed, missingHost, invalidDuration })
        {
            problems.Should().NotBeEmpty();
            string.Join("\n", problems).Should().NotContain(secret);
            EnvironmentValidator.Format(problems).Should().NotContain(secret);
        }
    }

    [Fact]
    public void Formatted_output_points_the_operator_to_the_fix()
    {
        var text = EnvironmentValidator.Format(["ConnectionStrings__DefaultConnection is not set."]);
        text.Should().Contain("will not start").And.Contain("README").And.Contain(".env.example");
    }
}
