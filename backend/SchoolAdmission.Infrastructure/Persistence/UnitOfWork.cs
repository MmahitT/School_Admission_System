using SchoolAdmission.Application.Abstractions;

namespace SchoolAdmission.Infrastructure.Persistence;

public class UnitOfWork(AdmissionDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        // READ COMMITTED (PostgreSQL default): each statement sees all rows committed before it starts,
        // which, combined with the row locks taken by the repository, makes the seat count exact.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result; // disposing without commit (exception path) rolls back
    }
}
