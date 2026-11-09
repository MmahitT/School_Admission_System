using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Application.Abstractions;

public record ClassSeats(Guid Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats);

public record StudentHoldRow(Guid HoldId, Guid ClassId, string ClassName, HoldStatus Status, DateTime ExpiresAt);

/// <summary>Persistence operations needed by the admission services. Implemented over PostgreSQL.</summary>
public interface IAdmissionRepository
{
    void AddClass(AdmissionClass admissionClass);
    void AddHold(AdmissionHold hold);

    /// <summary>Reads a class with a row lock (SELECT … FOR UPDATE). Must run inside a transaction.</summary>
    Task<AdmissionClass?> LockClassAsync(Guid classId, CancellationToken ct);

    /// <summary>Reads a hold with a row lock (SELECT … FOR UPDATE). Must run inside a transaction.</summary>
    Task<AdmissionHold?> LockHoldAsync(Guid holdId, CancellationToken ct);

    Task<AdmissionHold?> GetHoldAsync(Guid holdId, CancellationToken ct);

    Task<ClassSeats?> GetClassSeatsAsync(Guid classId, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<ClassSeats>> ListClassSeatsAsync(DateTime now, CancellationToken ct);

    /// <summary>HELD → EXPIRED for every hold with ExpiresAt &lt;= now. Returns rows changed.</summary>
    Task<int> ExpireStaleHoldsAsync(DateTime now, CancellationToken ct);

    /// <summary>Same, restricted to holds of the given class OR the given student.</summary>
    Task<int> ExpireStaleHoldsAsync(DateTime now, Guid classId, string studentId, CancellationToken ct);

    /// <summary>Same, restricted to one student's holds.</summary>
    Task<int> ExpireStaleHoldsForStudentAsync(DateTime now, string studentId, CancellationToken ct);

    /// <summary>CONFIRMED holds plus HELD holds that have not yet expired.</summary>
    Task<int> CountActiveSeatsAsync(Guid classId, DateTime now, CancellationToken ct);

    Task<bool> StudentHasHeldAsync(string studentId, CancellationToken ct);
    Task<bool> StudentHasApplicationAsync(Guid classId, string studentId, CancellationToken ct);
    Task<IReadOnlyList<StudentHoldRow>> ListStudentHoldsAsync(string studentId, CancellationToken ct);

    /// <summary>Persists changes, translating database constraint violations into <c>AdmissionException</c>.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
