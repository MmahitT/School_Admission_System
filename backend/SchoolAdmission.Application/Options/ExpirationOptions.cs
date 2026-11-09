namespace SchoolAdmission.Application.Options;

public class ExpirationOptions
{
    /// <summary>Configuration key (environment variable) that overrides <see cref="SweepIntervalSeconds"/>.</summary>
    public const string SweepIntervalKey = "EXPIRATION_SWEEP_INTERVAL_SECONDS";

    /// <summary>How often the background sweeper persists EXPIRED for time-expired holds.</summary>
    public int SweepIntervalSeconds { get; set; } = 5;
}
