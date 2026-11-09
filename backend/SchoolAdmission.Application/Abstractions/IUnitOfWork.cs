namespace SchoolAdmission.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>Runs <paramref name="work"/> in one database transaction: commit on success, roll back on any exception.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct);
}
