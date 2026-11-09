using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Infrastructure.Persistence;
using SchoolAdmission.Tests.Support;
using Xunit;

namespace SchoolAdmission.Tests.Database;

/// <summary>
/// The database constraints are a backstop behind the service checks. These tests bypass the services and prove
/// that each backstop is translated to the right business error, that transactions roll back, and that the
/// class row lock really serializes allocators.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RepositoryAndTransactionTests(PostgresFixture pg)
{
    private static readonly DateTime Now = new(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);
    private static long _counter = 20_270_000_000_000;
    private static string NewStudent() => Interlocked.Increment(ref _counter).ToString("D17");

    private (AdmissionDbContext Db, AdmissionRepository Repo, UnitOfWork Uow) NewScope()
    {
        var db = pg.CreateContext();
        return (db, new AdmissionRepository(db), new UnitOfWork(db));
    }

    private static AdmissionClass NewClass(int seats = 1, string? name = null) => new()
    {
        Id = Guid.NewGuid(), Name = name ?? $"C {Guid.NewGuid():N}", TotalSeats = seats, CreatedAt = Now, UpdatedAt = Now,
    };

    private static AdmissionHold NewHold(Guid classId, string? student = null, HoldStatus status = HoldStatus.Held) => new()
    {
        Id = Guid.NewGuid(), ClassId = classId, StudentId = student ?? NewStudent(), Status = status,
        CreatedAt = Now, ExpiresAt = Now.AddMinutes(10),
        CancelledAt = status == HoldStatus.Cancelled ? Now : null,
    };

    private async Task<AdmissionClass> SeedAsync(int seats = 1)
    {
        var (db, repo, _) = NewScope();
        await using var _ = db;
        var c = NewClass(seats);
        repo.AddClass(c);
        await repo.SaveChangesAsync(default);
        return c;
    }

    private async Task<AdmissionException> SaveFailsAsync(Action<AdmissionRepository> add)
    {
        var (db, repo, _) = NewScope();
        await using var _ = db;
        add(repo);
        var act = () => repo.SaveChangesAsync(default);
        return (await act.Should().ThrowAsync<AdmissionException>()).Which;
    }

    // ---- constraint → business error translation ----------------------------------------------------
    [Fact]
    public async Task Capacity_trigger_violation_becomes_NO_SEATS_AVAILABLE()
    {
        var c = await SeedAsync(seats: 1);
        var (db, repo, _) = NewScope();
        await using (db) { repo.AddHold(NewHold(c.Id)); await repo.SaveChangesAsync(default); }

        var ex = await SaveFailsAsync(r => r.AddHold(NewHold(c.Id)));
        (ex.Code, ex.Kind).Should().Be((ErrorCodes.NoSeatsAvailable, ErrorKind.Conflict));
    }

    [Fact]
    public async Task Second_held_hold_for_a_student_becomes_ACTIVE_HOLD_EXISTS()
    {
        var a = await SeedAsync(); var b = await SeedAsync();
        var student = NewStudent();
        var (db, repo, _) = NewScope();
        await using (db) { repo.AddHold(NewHold(a.Id, student)); await repo.SaveChangesAsync(default); }

        (await SaveFailsAsync(r => r.AddHold(NewHold(b.Id, student)))).Code.Should().Be(ErrorCodes.ActiveHoldExists);
    }

    [Fact]
    public async Task Repeat_application_to_a_class_becomes_ALREADY_APPLIED()
    {
        var c = await SeedAsync(seats: 5);
        var student = NewStudent();
        var (db, repo, _) = NewScope();
        await using (db) { repo.AddHold(NewHold(c.Id, student, HoldStatus.Cancelled)); await repo.SaveChangesAsync(default); }

        (await SaveFailsAsync(r => r.AddHold(NewHold(c.Id, student)))).Code.Should().Be(ErrorCodes.AlreadyApplied);
    }

    [Fact]
    public async Task Duplicate_class_name_becomes_DUPLICATE_CLASS_NAME()
    {
        var name = $"Dup {Guid.NewGuid():N}";
        var (db, repo, _) = NewScope();
        await using (db) { repo.AddClass(NewClass(name: name)); await repo.SaveChangesAsync(default); }

        (await SaveFailsAsync(r => r.AddClass(NewClass(name: name.ToLowerInvariant())))).Code.Should().Be(ErrorCodes.DuplicateClassName);
    }

    [Fact]
    public async Task Unexpected_database_errors_are_not_disguised_as_business_errors()
    {
        var c = await SeedAsync();
        var (db, repo, _) = NewScope();
        await using var _ = db;
        repo.AddHold(NewHold(c.Id, "123")); // violates the student id CHECK/column type, which is not a mapped rule
        var act = () => repo.SaveChangesAsync(default);
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ---- transactions -------------------------------------------------------------------------------
    [Fact]
    public async Task Transaction_commits_on_success_and_returns_the_result()
    {
        var (db, repo, uow) = NewScope();
        await using var _ = db;
        var c = NewClass();
        var result = await uow.ExecuteInTransactionAsync(async () =>
        {
            repo.AddClass(c);
            await repo.SaveChangesAsync(default);
            return 42;
        }, default);

        result.Should().Be(42);
        await using var check = pg.CreateContext();
        (await check.Classes.AnyAsync(x => x.Id == c.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Transaction_rolls_back_everything_when_the_work_throws()
    {
        var (db, repo, uow) = NewScope();
        await using var _ = db;
        var c = NewClass(seats: 3);
        var hold = NewHold(c.Id);

        var act = () => uow.ExecuteInTransactionAsync<int>(async () =>
        {
            repo.AddClass(c);
            await repo.SaveChangesAsync(default);
            repo.AddHold(hold);
            await repo.SaveChangesAsync(default);       // both rows written inside the transaction…
            throw new InvalidOperationException("boom"); // …then the work fails
        }, default);
        await act.Should().ThrowAsync<InvalidOperationException>();

        await using var check = pg.CreateContext();
        (await check.Classes.AnyAsync(x => x.Id == c.Id)).Should().BeFalse("the class insert must be rolled back");
        (await check.Holds.AnyAsync(x => x.Id == hold.Id)).Should().BeFalse("the hold insert must be rolled back");
    }

    [Fact]
    public async Task Class_row_lock_blocks_a_second_allocator_until_the_first_commits()
    {
        var c = await SeedAsync(seats: 5);
        var (db1, repo1, uow1) = NewScope();
        var (db2, repo2, uow2) = NewScope();
        await using var _1 = db1; await using var _2 = db2;

        var firstHasLock = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();
        var first = uow1.ExecuteInTransactionAsync(async () =>
        {
            await repo1.LockClassAsync(c.Id, default);
            firstHasLock.SetResult();
            await releaseFirst.Task;
            return 1;
        }, default);
        await firstHasLock.Task;

        var secondGotLock = false;
        var second = uow2.ExecuteInTransactionAsync(async () =>
        {
            await repo2.LockClassAsync(c.Id, default);
            secondGotLock = true;
            return 2;
        }, default);

        await Task.Delay(500);
        secondGotLock.Should().BeFalse("FOR UPDATE must make the second allocator wait");

        releaseFirst.SetResult();
        await first;
        (await second).Should().Be(2);
        secondGotLock.Should().BeTrue();
    }

    [Fact]
    public async Task Locks_on_different_classes_do_not_block_each_other()
    {
        var a = await SeedAsync(); var b = await SeedAsync();
        var (db1, repo1, uow1) = NewScope();
        var (db2, repo2, uow2) = NewScope();
        await using var _1 = db1; await using var _2 = db2;

        var release = new TaskCompletionSource();
        var held = new TaskCompletionSource();
        var first = uow1.ExecuteInTransactionAsync(async () => { await repo1.LockClassAsync(a.Id, default); held.SetResult(); await release.Task; return 1; }, default);
        await held.Task;

        var second = uow2.ExecuteInTransactionAsync(async () => { await repo2.LockClassAsync(b.Id, default); return 2; }, default);
        (await Task.WhenAny(second, Task.Delay(2000))).Should().BeSameAs(second, "independent classes must not contend");

        release.SetResult();
        await first;
    }

    [Fact]
    public async Task Lock_on_a_missing_class_or_hold_returns_null()
    {
        var (db, repo, uow) = NewScope();
        await using var _ = db;
        await uow.ExecuteInTransactionAsync(async () =>
        {
            (await repo.LockClassAsync(Guid.NewGuid(), default)).Should().BeNull();
            (await repo.LockHoldAsync(Guid.NewGuid(), default)).Should().BeNull();
            return 0;
        }, default);
    }
}
