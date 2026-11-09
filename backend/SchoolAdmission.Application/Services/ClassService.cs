using SchoolAdmission.Application.Abstractions;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Application.Contracts;
using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Application.Services;

public class ClassService(IAdmissionRepository repository, TimeProvider clock) : IClassService
{
    public const int MaxNameLength = 200;
    public const int MaxTotalSeats = 100_000;

    public async Task<ClassResponse> CreateAsync(CreateClassRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw AdmissionException.Validation("Class name is required.");
        if (name.Length > MaxNameLength)
            throw AdmissionException.Validation($"Class name must be at most {MaxNameLength} characters.");
        if (request.TotalSeats <= 0)
            throw AdmissionException.Validation("Total seats must be greater than zero.");
        if (request.TotalSeats > MaxTotalSeats)
            throw AdmissionException.Validation($"Total seats must be at most {MaxTotalSeats}.");

        var now = clock.GetUtcNow().UtcDateTime;
        var entity = new AdmissionClass
        {
            Id = Guid.NewGuid(), Name = name, TotalSeats = request.TotalSeats, CreatedAt = now, UpdatedAt = now,
        };
        repository.AddClass(entity);
        await repository.SaveChangesAsync(ct); // unique-name violation → DUPLICATE_CLASS_NAME
        return new ClassResponse(entity.Id, entity.Name, entity.TotalSeats);
    }

    public async Task<IReadOnlyList<ClassDetailsResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await repository.ListClassSeatsAsync(clock.GetUtcNow().UtcDateTime, ct);
        return rows.Select(Map).ToList();
    }

    public async Task<ClassDetailsResponse> GetAsync(Guid classId, CancellationToken ct)
    {
        var row = await repository.GetClassSeatsAsync(classId, clock.GetUtcNow().UtcDateTime, ct)
                  ?? throw AdmissionException.ClassNotFound();
        return Map(row);
    }

    internal static ClassDetailsResponse Map(ClassSeats r) => new(
        r.Id, r.Name, r.TotalSeats, r.HeldSeats, r.ConfirmedSeats,
        Math.Max(0, r.TotalSeats - r.HeldSeats - r.ConfirmedSeats));
}
