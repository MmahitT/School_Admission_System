using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchoolAdmission.Application.Abstractions;
using SchoolAdmission.Application.Options;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Infrastructure.BackgroundServices;
using SchoolAdmission.Tests.Support;
using Xunit;

namespace SchoolAdmission.Tests.Integration;

/// <summary>Expiration: the sweeper, its races with API calls, and its lifecycle. Real PostgreSQL.</summary>
[Collection(ApiCollection.Name)]
public class ExpirationTests(ApiFixture api, Xunit.Abstractions.ITestOutputHelper output)
{
    private HoldExpirationService NewSweeper(int intervalSeconds = 1, TimeProvider? clock = null) => new(
        api.Services.GetRequiredService<IServiceScopeFactory>(),
        clock ?? api.Clock,
        Microsoft.Extensions.Options.Options.Create(new ExpirationOptions { SweepIntervalSeconds = intervalSeconds }),
        NullLogger<HoldExpirationService>.Instance);

    private async Task<HoldStatus> StatusAsync(Guid holdId)
    {
        await using var db = api.CreateDb();
        return (await db.Holds.AsNoTracking().SingleAsync(h => h.Id == holdId)).Status;
    }

    private DateTime Now => api.Clock.GetUtcNow().UtcDateTime;

    // ---- single sweep -------------------------------------------------------------------------------
    [Fact]
    public async Task Sweep_persists_EXPIRED_only_for_time_expired_held_holds()
    {
        var classId = await api.CreateClassAsync(10);
        var stale = await api.ApplyOkAsync(classId, api.NewStudentId());
        var confirmedStudent = api.NewStudentId();
        var confirmed = await api.ApplyOkAsync(classId, confirmedStudent);
        (await api.ConfirmAsync(confirmed.HoldId, confirmedStudent)).EnsureSuccessStatusCode();

        api.Clock.Advance(TimeSpan.FromSeconds(ApiFixture.HoldSeconds - 100));
        var fresh = await api.ApplyOkAsync(classId, api.NewStudentId()); // expires 600 s after "now"
        api.Clock.Advance(TimeSpan.FromSeconds(101));                    // stale + confirmed are now past their expiry time

        var changed = await NewSweeper().SweepAsync(Now, default);

        changed.Should().BeGreaterThanOrEqualTo(1);
        (await StatusAsync(stale.HoldId)).Should().Be(HoldStatus.Expired);
        (await StatusAsync(confirmed.HoldId)).Should().Be(HoldStatus.Confirmed, "confirmed admissions never expire");
        (await StatusAsync(fresh.HoldId)).Should().Be(HoldStatus.Held, "not yet past its expiry");
    }

    [Fact]
    public async Task Sweep_is_idempotent_and_released_seats_become_available()
    {
        var classId = await api.CreateClassAsync(1);
        var hold = await api.ApplyOkAsync(classId, api.NewStudentId());
        api.Clock.Advance(TimeSpan.FromSeconds(ApiFixture.HoldSeconds));

        var sweeper = NewSweeper();
        (await sweeper.SweepAsync(Now, default)).Should().BeGreaterThanOrEqualTo(1);
        (await sweeper.SweepAsync(Now, default)).Should().Be(0);

        (await StatusAsync(hold.HoldId)).Should().Be(HoldStatus.Expired);
        var details = await api.GetClassAsync(classId);
        (details.HeldSeats, details.AvailableSeats).Should().Be((0, 1));
        (await api.ApplyAsync(classId, api.NewStudentId())).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- races --------------------------------------------------------------------------------------
    [Fact]
    public async Task Confirmation_racing_the_sweeper_always_ends_consistently()
    {
        // One contest per hold: confirm and sweep are released at the same instant. Confirm decides with the
        // API clock (hold still valid); the sweeper uses a clock already past expiry, so either may win.
        const int rounds = 80;
        var sweeper = NewSweeper();
        int confirmedCount = 0, expiredCount = 0;

        for (var i = 0; i < rounds; i++)
        {
            var student = api.NewStudentId();
            var hold = await api.ApplyOkAsync(await api.CreateClassAsync(1), student);
            var sweepNow = Now.AddSeconds(ApiFixture.HoldSeconds + 1);

            using var gate = new ManualResetEventSlim(false);
            var confirm = Task.Run(() => { gate.Wait(); return api.ConfirmAsync(hold.HoldId, student); });
            var sweep = Task.Run(() => { gate.Wait(); return sweeper.SweepAsync(sweepNow, default); });
            await Task.Delay(20);
            gate.Set();
            var response = await confirm;
            await sweep;

            await using var db = api.CreateDb();
            var row = await db.Holds.AsNoTracking().SingleAsync(h => h.Id == hold.HoldId);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                confirmedCount++;
                row.Status.Should().Be(HoldStatus.Confirmed, "a 200 means the admission really is confirmed (round {0})", i);
                row.ConfirmedAt.Should().NotBeNull();
            }
            else
            {
                expiredCount++;
                response.StatusCode.Should().Be(HttpStatusCode.Conflict, "no 5xx");
                (await ApiFixture.ErrorCodeAsync(response)).Should().Be("HOLD_EXPIRED");
                await sweeper.SweepAsync(sweepNow, default);
                (await StatusAsync(hold.HoldId)).Should().Be(HoldStatus.Expired, "a 409 means the hold is expired, never confirmed");
                row.ConfirmedAt.Should().BeNull();
            }
        }
        output.WriteLine($"confirm-vs-sweeper over {rounds} rounds: {confirmedCount} confirm won, {expiredCount} sweeper won");
    }

    [Fact]
    public async Task Applications_racing_the_sweeper_on_a_full_class_get_exactly_one_seat()
    {
        var classId = await api.CreateClassAsync(1);
        var first = await api.ApplyOkAsync(classId, api.NewStudentId());
        api.Clock.Advance(TimeSpan.FromSeconds(ApiFixture.HoldSeconds)); // first hold is now past expiry (unswept)

        var sweeper = NewSweeper();
        using var stop = new CancellationTokenSource();
        var sweeping = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested) await sweeper.SweepAsync(Now, default);
        });
        var students = Enumerable.Range(0, 30).Select(_ => api.NewStudentId()).ToArray();
        using var gate = new ManualResetEventSlim(false);
        var tasks = students.Select(s => Task.Run(() => { gate.Wait(); return api.ApplyAsync(classId, s); })).ToArray();
        await Task.Delay(100);
        gate.Set();
        var responses = await Task.WhenAll(tasks);
        stop.Cancel();
        await sweeping;

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        await using var db = api.CreateDb();
        (await db.Holds.CountAsync(h => h.ClassId == classId && h.Status == HoldStatus.Held)).Should().Be(1);
        (await StatusAsync(first.HoldId)).Should().Be(HoldStatus.Expired);
    }

    // ---- background service lifecycle (real time) ---------------------------------------------------
    [Fact]
    public async Task Running_service_catches_up_on_start_and_then_expires_holds_on_its_own()
    {
        // Rows are written directly with real wall-clock times, independent of the fake API clock.
        var real = DateTime.UtcNow;
        var classId = await api.CreateClassAsync(5);
        var alreadyStale = Guid.NewGuid();
        var expiresSoon = Guid.NewGuid();
        await using (var db = api.CreateDb())
        {
            db.Holds.Add(NewHold(alreadyStale, classId, real.AddMinutes(-10), real.AddSeconds(-5)));
            db.Holds.Add(NewHold(expiresSoon, classId, real, real.AddSeconds(2)));
            await db.SaveChangesAsync();
        }

        var service = NewSweeper(intervalSeconds: 1, clock: TimeProvider.System);
        await service.StartAsync(default);
        try
        {
            await WaitUntilAsync(async () => await StatusAsync(alreadyStale) == HoldStatus.Expired, TimeSpan.FromSeconds(5));
            (await StatusAsync(expiresSoon)).Should().Be(HoldStatus.Held, "still inside its hold window right after start");
            await WaitUntilAsync(async () => await StatusAsync(expiresSoon) == HoldStatus.Expired, TimeSpan.FromSeconds(8));
        }
        finally
        {
            var stopping = service.StopAsync(default);
            (await Task.WhenAny(stopping, Task.Delay(3000))).Should().BeSameAs(stopping, "shutdown must be prompt");
        }
    }

    [Fact]
    public async Task A_failing_sweep_does_not_kill_the_service()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddScoped<IAdmissionRepository>(_ => new FlakyRepository(() => Interlocked.Increment(ref calls)));
        await using var provider = services.BuildServiceProvider();
        var service = new HoldExpirationService(
            provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new ExpirationOptions { SweepIntervalSeconds = 1 }),
            NullLogger<HoldExpirationService>.Instance);

        await service.StartAsync(default);
        await WaitUntilAsync(() => Task.FromResult(Volatile.Read(ref calls) >= 3), TimeSpan.FromSeconds(8));
        await service.StopAsync(default);

        calls.Should().BeGreaterThanOrEqualTo(3, "the first call threw, yet sweeping continued");
    }

    private static AdmissionHold NewHold(Guid id, Guid classId, DateTime created, DateTime expires) => new()
    {
        Id = id, ClassId = classId, StudentId = Random.Shared.NextInt64(30_000_000_000_000_000, 39_999_999_999_999_999).ToString("D17"),
        Status = HoldStatus.Held, CreatedAt = created, ExpiresAt = expires,
    };

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        (await condition()).Should().BeTrue($"condition should become true within {timeout.TotalSeconds}s");
    }

    /// <summary>Throws on the first sweep, succeeds afterwards.</summary>
    private sealed class FlakyRepository(Func<int> onCall) : FakeAdmissionRepository
    {
        public override Task<int> ExpireStaleHoldsAsync(DateTime now, CancellationToken ct)
        {
            if (onCall() == 1) throw new InvalidOperationException("database unavailable");
            return Task.FromResult(0);
        }
    }
}
