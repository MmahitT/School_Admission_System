using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Application.Abstractions;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Infrastructure.Persistence;

public class AdmissionRepository(AdmissionDbContext db) : IAdmissionRepository
{
    public void AddClass(AdmissionClass admissionClass) => db.Classes.Add(admissionClass);

    public void AddHold(AdmissionHold hold) => db.Holds.Add(hold);

    public async Task<AdmissionClass?> LockClassAsync(Guid classId, CancellationToken ct)
    {
        // Raw, parameterized SQL: EF has no FOR UPDATE operator. ToListAsync (not SingleOrDefault) keeps
        // the statement from being wrapped in a subquery.
        var rows = await db.Classes
            .FromSqlInterpolated($"SELECT * FROM admission_classes WHERE id = {classId} FOR UPDATE")
            .ToListAsync(ct);
        return rows.FirstOrDefault();
    }

    public async Task<AdmissionHold?> LockHoldAsync(Guid holdId, CancellationToken ct)
    {
        var rows = await db.Holds
            .FromSqlInterpolated($"SELECT * FROM admission_holds WHERE id = {holdId} FOR UPDATE")
            .ToListAsync(ct);
        return rows.FirstOrDefault();
    }

    public Task<AdmissionHold?> GetHoldAsync(Guid holdId, CancellationToken ct) =>
        db.Holds.AsNoTracking().FirstOrDefaultAsync(h => h.Id == holdId, ct);

    public Task<ClassSeats?> GetClassSeatsAsync(Guid classId, DateTime now, CancellationToken ct) =>
        ToSeats(db.Classes.Where(c => c.Id == classId), now).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ClassSeats>> ListClassSeatsAsync(DateTime now, CancellationToken ct) =>
        await ToSeats(db.Classes.OrderBy(c => c.Name), now).ToListAsync(ct);

    // One statement => one consistent snapshot of held/confirmed counts. Filters/ordering are applied
    // to the entity query *before* projecting (EF cannot filter on a constructor-built projection).
    private static IQueryable<ClassSeats> ToSeats(IQueryable<AdmissionClass> classes, DateTime now) =>
        classes.AsNoTracking().Select(c => new ClassSeats(
            c.Id, c.Name, c.TotalSeats,
            c.Holds.Count(h => h.Status == HoldStatus.Held && h.ExpiresAt > now),
            c.Holds.Count(h => h.Status == HoldStatus.Confirmed)));

    public Task<int> ExpireStaleHoldsAsync(DateTime now, CancellationToken ct) =>
        db.Holds.Where(h => h.Status == HoldStatus.Held && h.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Expired), ct);

    public Task<int> ExpireStaleHoldsAsync(DateTime now, Guid classId, string studentId, CancellationToken ct) =>
        db.Holds.Where(h => h.Status == HoldStatus.Held && h.ExpiresAt <= now
                            && (h.ClassId == classId || h.StudentId == studentId))
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Expired), ct);

    public Task<int> ExpireStaleHoldsForStudentAsync(DateTime now, string studentId, CancellationToken ct) =>
        db.Holds.Where(h => h.Status == HoldStatus.Held && h.ExpiresAt <= now && h.StudentId == studentId)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Expired), ct);

    public Task<int> CountActiveSeatsAsync(Guid classId, DateTime now, CancellationToken ct) =>
        db.Holds.CountAsync(h => h.ClassId == classId
            && (h.Status == HoldStatus.Confirmed || (h.Status == HoldStatus.Held && h.ExpiresAt > now)), ct);

    public Task<bool> StudentHasHeldAsync(string studentId, CancellationToken ct) =>
        db.Holds.AnyAsync(h => h.StudentId == studentId && h.Status == HoldStatus.Held, ct);

    public Task<bool> StudentHasApplicationAsync(Guid classId, string studentId, CancellationToken ct) =>
        db.Holds.AnyAsync(h => h.ClassId == classId && h.StudentId == studentId
            && (h.Status == HoldStatus.Held || h.Status == HoldStatus.Confirmed || h.Status == HoldStatus.Cancelled), ct);

    public async Task<IReadOnlyList<StudentHoldRow>> ListStudentHoldsAsync(string studentId, CancellationToken ct) =>
        await db.Holds.AsNoTracking()
            .Where(h => h.StudentId == studentId)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new StudentHoldRow(h.Id, h.ClassId, h.Class!.Name, h.Status, h.ExpiresAt))
            .ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && Translate(pg) is { } mapped)
        {
            throw mapped;
        }
    }

    /// <summary>Database backstops for the business rules surface as the same errors the services raise.</summary>
    private static AdmissionException? Translate(PostgresException pg) => (pg.SqlState, pg.ConstraintName) switch
    {
        (PostgresErrorCodes.UniqueViolation, "ux_admission_holds_one_held_per_student") => AdmissionException.ActiveHoldExists(),
        (PostgresErrorCodes.UniqueViolation, "ux_admission_holds_one_application_per_class") => AdmissionException.AlreadyApplied(),
        (PostgresErrorCodes.UniqueViolation, "ux_admission_classes_name_lower") => AdmissionException.DuplicateClassName(),
        (PostgresErrorCodes.CheckViolation, "trg_admission_holds_capacity") => AdmissionException.NoSeats(),
        _ => null,
    };
}
