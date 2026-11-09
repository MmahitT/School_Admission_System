using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolAdmission.Application.Abstractions;
using SchoolAdmission.Application.Options;

namespace SchoolAdmission.Infrastructure.BackgroundServices;

/// <summary>
/// Persists EXPIRED for holds whose time has run out. This only normalizes stored status: every API path
/// already treats a time-expired HELD hold as expired, so correctness never depends on this service.
/// </summary>
public sealed class HoldExpirationService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<ExpirationOptions> options,
    ILogger<HoldExpirationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.SweepIntervalSeconds);
        logger.LogInformation("Hold expiration sweeper started (every {Interval}s)", interval.TotalSeconds);

        // Catch up immediately: holds may have expired while the service was down.
        await SweepSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepSafelyAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        logger.LogInformation("Hold expiration sweeper stopped");
    }

    /// <summary>One sweep with an explicit "now". A single atomic UPDATE; safe to run concurrently with API calls.</summary>
    public async Task<int> SweepAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAdmissionRepository>();
        return await repository.ExpireStaleHoldsAsync(nowUtc, ct);
    }

    private async Task SweepSafelyAsync(CancellationToken ct)
    {
        try
        {
            var expired = await SweepAsync(clock.GetUtcNow().UtcDateTime, ct);
            if (expired > 0) logger.LogInformation("Expired {Count} stale hold(s)", expired);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Never let one failed sweep (e.g. database restarting) kill the service.
            logger.LogError(ex, "Expiration sweep failed; will retry in {Interval}s", options.Value.SweepIntervalSeconds);
        }
    }
}
