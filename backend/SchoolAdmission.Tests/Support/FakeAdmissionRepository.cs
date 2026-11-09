using SchoolAdmission.Application.Abstractions;
using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Tests.Support;

/// <summary>In-memory stand-in used only for unit-testing service rules (no concurrency semantics).</summary>
public class FakeAdmissionRepository : IAdmissionRepository, IUnitOfWork
{
    public List<AdmissionClass> Classes { get; } = [];
    public List<AdmissionHold> Holds { get; } = [];

    public void AddClass(AdmissionClass c) => Classes.Add(c);
    public void AddHold(AdmissionHold h) => Holds.Add(h);
    public Task<AdmissionClass?> LockClassAsync(Guid id, CancellationToken ct) => Task.FromResult(Classes.FirstOrDefault(c => c.Id == id));
    public Task<AdmissionHold?> LockHoldAsync(Guid id, CancellationToken ct) => Task.FromResult(Holds.FirstOrDefault(h => h.Id == id));
    public Task<AdmissionHold?> GetHoldAsync(Guid id, CancellationToken ct) => LockHoldAsync(id, ct);

    private ClassSeats Seats(AdmissionClass c, DateTime now) => new(c.Id, c.Name, c.TotalSeats,
        Holds.Count(h => h.ClassId == c.Id && h.Status == HoldStatus.Held && h.ExpiresAt > now),
        Holds.Count(h => h.ClassId == c.Id && h.Status == HoldStatus.Confirmed));

    public Task<ClassSeats?> GetClassSeatsAsync(Guid id, DateTime now, CancellationToken ct) =>
        Task.FromResult(Classes.Where(c => c.Id == id).Select(c => Seats(c, now)).FirstOrDefault());

    public Task<IReadOnlyList<ClassSeats>> ListClassSeatsAsync(DateTime now, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ClassSeats>>(Classes.Select(c => Seats(c, now)).ToList());

    private int Expire(Func<AdmissionHold, bool> filter, DateTime now)
    {
        var stale = Holds.Where(h => h.Status == HoldStatus.Held && h.ExpiresAt <= now && filter(h)).ToList();
        stale.ForEach(h => h.Status = HoldStatus.Expired);
        return stale.Count;
    }

    public virtual Task<int> ExpireStaleHoldsAsync(DateTime now, CancellationToken ct) => Task.FromResult(Expire(_ => true, now));
    public Task<int> ExpireStaleHoldsAsync(DateTime now, Guid classId, string studentId, CancellationToken ct) =>
        Task.FromResult(Expire(h => h.ClassId == classId || h.StudentId == studentId, now));
    public Task<int> ExpireStaleHoldsForStudentAsync(DateTime now, string studentId, CancellationToken ct) =>
        Task.FromResult(Expire(h => h.StudentId == studentId, now));

    public Task<int> CountActiveSeatsAsync(Guid classId, DateTime now, CancellationToken ct) =>
        Task.FromResult(Holds.Count(h => h.ClassId == classId
            && (h.Status == HoldStatus.Confirmed || (h.Status == HoldStatus.Held && h.ExpiresAt > now))));

    public Task<bool> StudentHasHeldAsync(string s, CancellationToken ct) =>
        Task.FromResult(Holds.Any(h => h.StudentId == s && h.Status == HoldStatus.Held));

    public Task<bool> StudentHasApplicationAsync(Guid classId, string s, CancellationToken ct) =>
        Task.FromResult(Holds.Any(h => h.ClassId == classId && h.StudentId == s && h.Status != HoldStatus.Expired));

    public Task<IReadOnlyList<StudentHoldRow>> ListStudentHoldsAsync(string s, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StudentHoldRow>>(Holds.Where(h => h.StudentId == s)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new StudentHoldRow(h.Id, h.ClassId, Classes.First(c => c.Id == h.ClassId).Name, h.Status, h.ExpiresAt))
            .ToList());

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) => work();
}
