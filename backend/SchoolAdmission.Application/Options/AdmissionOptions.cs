namespace SchoolAdmission.Application.Options;

public class AdmissionOptions
{
    /// <summary>Configuration key (environment variable) that overrides <see cref="HoldDurationSeconds"/>.</summary>
    public const string HoldDurationKey = "HOLD_DURATION_SECONDS";

    public int HoldDurationSeconds { get; set; } = 600;
}
