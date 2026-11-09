using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolAdmission.Application.Abstractions;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Application.Contracts;
using SchoolAdmission.Application.Options;
using SchoolAdmission.Domain;
using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Application.Services;

public class HoldService(
    IAdmissionRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<AdmissionOptions> options,
    ILogger<HoldService> logger) : IHoldService
{
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Atomic seat allocation. The class row is locked (FOR UPDATE) so concurrent applicants for the same
    /// class are serialized; the count below therefore always sees every committed hold.
    /// </summary>
    public async Task<CreateHoldResponse> CreateAsync(Guid classId, string? studentId, CancellationToken ct)
    {
        var student = RequireStudent(studentId);

        var hold = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var admissionClass = await repository.LockClassAsync(classId, ct)
                                 ?? throw AdmissionException.ClassNotFound();

            // Read the clock after the lock is granted so queued requests use a fresh time.
            var now = UtcNow;

            // Stale (time-expired but not yet swept) holds must not consume capacity or trip the
            // per-student unique index.
            await repository.ExpireStaleHoldsAsync(now, classId, student, ct);

            if (await repository.StudentHasHeldAsync(student, ct))
                throw AdmissionException.ActiveHoldExists();
            if (await repository.StudentHasApplicationAsync(classId, student, ct))
                throw AdmissionException.AlreadyApplied();

            var active = await repository.CountActiveSeatsAsync(classId, now, ct);
            if (admissionClass.TotalSeats - active <= 0)
                throw AdmissionException.NoSeats();

            var created = new AdmissionHold
            {
                Id = Guid.NewGuid(),
                ClassId = classId,
                StudentId = student,
                Status = HoldStatus.Held,
                CreatedAt = now,
                ExpiresAt = now.AddSeconds(options.Value.HoldDurationSeconds),
            };
            repository.AddHold(created);
            await repository.SaveChangesAsync(ct);
            return created;
        }, ct);

        logger.LogInformation("Hold {HoldId} created for class {ClassId} (expires {ExpiresAt:o})",
            hold.Id, hold.ClassId, hold.ExpiresAt);
        return new CreateHoldResponse(hold.Id, hold.ClassId, hold.StudentId, "HELD", hold.ExpiresAt);
    }

    public async Task<HoldResponse> GetAsync(Guid holdId, CancellationToken ct)
    {
        var hold = await repository.GetHoldAsync(holdId, ct) ?? throw AdmissionException.HoldNotFound();
        return new HoldResponse(hold.Id, hold.ClassId, EffectiveStatus(hold.Status, hold.ExpiresAt, UtcNow), hold.ExpiresAt);
    }

    public async Task<ConfirmHoldResponse> ConfirmAsync(Guid holdId, string? studentId, CancellationToken ct)
    {
        var student = RequireStudent(studentId);

        // Outcome is returned (not thrown) so that marking an expired hold EXPIRED is committed.
        var outcome = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var hold = await repository.LockHoldAsync(holdId, ct) ?? throw AdmissionException.HoldNotFound();
            if (hold.StudentId != student) throw AdmissionException.NotHoldOwner();

            var now = UtcNow;
            switch (hold.Status)
            {
                case HoldStatus.Confirmed: return Outcome.Ok; // idempotent: no state change
                case HoldStatus.Cancelled: return Outcome.Cancelled;
                case HoldStatus.Expired: return Outcome.Expired;
            }

            if (hold.ExpiresAt <= now)
            {
                hold.Status = HoldStatus.Expired;
                await repository.SaveChangesAsync(ct);
                return Outcome.Expired;
            }

            hold.Status = HoldStatus.Confirmed;
            hold.ConfirmedAt = now;
            await repository.SaveChangesAsync(ct);
            return Outcome.Confirmed;
        }, ct);

        switch (outcome)
        {
            case Outcome.Expired: throw AdmissionException.HoldExpired();
            case Outcome.Cancelled: throw AdmissionException.HoldCancelled();
            case Outcome.Confirmed: logger.LogInformation("Hold {HoldId} confirmed", holdId); break;
        }
        return new ConfirmHoldResponse(holdId, "CONFIRMED");
    }

    public async Task CancelAsync(Guid holdId, string? studentId, CancellationToken ct)
    {
        var student = RequireStudent(studentId);

        var outcome = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var hold = await repository.LockHoldAsync(holdId, ct) ?? throw AdmissionException.HoldNotFound();
            if (hold.StudentId != student) throw AdmissionException.NotHoldOwner();

            var now = UtcNow;
            switch (hold.Status)
            {
                case HoldStatus.Confirmed: return Outcome.Confirmed;
                case HoldStatus.Cancelled: return Outcome.Ok; // idempotent
                case HoldStatus.Expired: return Outcome.Expired;
            }

            if (hold.ExpiresAt <= now)
            {
                hold.Status = HoldStatus.Expired;
                await repository.SaveChangesAsync(ct);
                return Outcome.Expired;
            }

            // Releasing a seat can never cause overbooking, so no class lock is needed.
            hold.Status = HoldStatus.Cancelled;
            hold.CancelledAt = now;
            await repository.SaveChangesAsync(ct);
            return Outcome.Cancelled;
        }, ct);

        switch (outcome)
        {
            case Outcome.Confirmed: throw AdmissionException.HoldConfirmed();
            case Outcome.Expired: throw AdmissionException.HoldExpired();
            case Outcome.Cancelled: logger.LogInformation("Hold {HoldId} cancelled", holdId); break;
        }
    }

    public async Task<IReadOnlyList<MyHoldResponse>> ListForStudentAsync(string? studentId, CancellationToken ct)
    {
        var student = RequireStudent(studentId);
        var now = UtcNow;

        // Persist expiry for this student's stale holds, then read.
        await repository.ExpireStaleHoldsForStudentAsync(now, student, ct);
        var rows = await repository.ListStudentHoldsAsync(student, ct);
        return rows
            .Select(r => new MyHoldResponse(r.HoldId, r.ClassId, r.ClassName,
                EffectiveStatus(r.Status, r.ExpiresAt, now), r.ExpiresAt))
            .ToList();
    }

    private static string RequireStudent(string? studentId) =>
        StudentId.IsValid(studentId) ? studentId! : throw AdmissionException.InvalidStudentId();

    /// <summary>A HELD hold past its expiry is always reported EXPIRED, whether or not the sweeper has run.</summary>
    internal static string EffectiveStatus(HoldStatus status, DateTime expiresAt, DateTime now) =>
        status == HoldStatus.Held && expiresAt <= now ? "EXPIRED" : status.ToString().ToUpperInvariant();

    private enum Outcome { Ok, Confirmed, Cancelled, Expired }
}
