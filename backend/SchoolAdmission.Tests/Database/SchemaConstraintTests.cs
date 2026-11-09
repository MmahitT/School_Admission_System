using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Infrastructure.Persistence;
using SchoolAdmission.Tests.Support;
using Xunit;

namespace SchoolAdmission.Tests.Database;

/// <summary>Proves the PostgreSQL schema itself enforces the business rules (spec section 5.3).</summary>
[Collection(PostgresCollection.Name)]
public class SchemaConstraintTests(PostgresFixture pg)
{
    private static readonly DateTime Now = new(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);
    private static long _studentCounter = 20_150_000_000_000;

    private static string NewStudentId() => Interlocked.Increment(ref _studentCounter).ToString("D17");

    private static AdmissionClass NewClass(int seats = 10, string? name = null) => new()
    {
        Id = Guid.NewGuid(), Name = name ?? $"Class {Guid.NewGuid():N}", TotalSeats = seats, CreatedAt = Now, UpdatedAt = Now,
    };

    private static AdmissionHold NewHold(Guid classId, string? studentId = null, HoldStatus status = HoldStatus.Held,
        DateTime? expiresAt = null) => new()
    {
        Id = Guid.NewGuid(), ClassId = classId, StudentId = studentId ?? NewStudentId(), Status = status,
        CreatedAt = Now, ExpiresAt = expiresAt ?? Now.AddMinutes(10),
        ConfirmedAt = status == HoldStatus.Confirmed ? Now : null,
        CancelledAt = status == HoldStatus.Cancelled ? Now : null,
    };

    private async Task<AdmissionClass> SeedClassAsync(int seats = 10)
    {
        await using var db = pg.CreateContext();
        var c = NewClass(seats);
        db.Classes.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private async Task<string> SqlStateOfAsync(Func<AdmissionDbContext, Task> action)
    {
        await using var db = pg.CreateContext();
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => action(db));
        return ex.InnerException.Should().BeOfType<PostgresException>().Subject.SqlState;
    }

    private Task<string> InsertHoldFailureAsync(AdmissionHold hold) => SqlStateOfAsync(async db =>
    {
        db.Holds.Add(hold);
        await db.SaveChangesAsync();
    });

    [Fact]
    public async Task Data_persists_across_contexts()
    {
        var c = await SeedClassAsync(5);
        var hold = NewHold(c.Id);
        await using (var db = pg.CreateContext()) { db.Holds.Add(hold); await db.SaveChangesAsync(); }

        await using var fresh = pg.CreateContext();
        var loaded = await fresh.Holds.SingleAsync(h => h.Id == hold.Id);
        loaded.Status.Should().Be(HoldStatus.Held);
        loaded.StudentId.Should().Be(hold.StudentId);
        loaded.ExpiresAt.Should().Be(hold.ExpiresAt);
    }

    [Fact]
    public async Task Status_is_stored_as_uppercase_text()
    {
        var c = await SeedClassAsync();
        var hold = NewHold(c.Id);
        await using var db = pg.CreateContext();
        db.Holds.Add(hold);
        await db.SaveChangesAsync();
        var raw = await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM admission_holds WHERE id = {hold.Id}").SingleAsync();
        raw.Should().Be("HELD");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Class_with_non_positive_seats_is_rejected(int seats)
    {
        var state = await SqlStateOfAsync(async db => { db.Classes.Add(NewClass(seats)); await db.SaveChangesAsync(); });
        state.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Class_name_is_unique_case_insensitively()
    {
        var name = $"Class 6 {Guid.NewGuid():N}";
        await using (var db = pg.CreateContext()) { db.Classes.Add(NewClass(name: name)); await db.SaveChangesAsync(); }
        var state = await SqlStateOfAsync(async db => { db.Classes.Add(NewClass(name: name.ToUpperInvariant())); await db.SaveChangesAsync(); });
        state.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Theory]
    [InlineData("1234567890123456")]   // 16 digits
    [InlineData("123456789012345678")] // 18 digits
    [InlineData("1234567890123456a")]  // letter
    [InlineData("                 ")]   // blanks
    public async Task Invalid_student_id_is_rejected(string studentId)
    {
        var c = await SeedClassAsync();
        // Too long is stopped by the character(17) column type (22001); everything else by the CHECK (23514).
        (await InsertHoldFailureAsync(NewHold(c.Id, studentId))).Should()
            .BeOneOf(PostgresErrorCodes.CheckViolation, PostgresErrorCodes.StringDataRightTruncation);
    }

    [Fact]
    public async Task Hold_for_missing_class_is_rejected()
    {
        (await InsertHoldFailureAsync(NewHold(Guid.NewGuid()))).Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Expiry_must_be_after_creation()
    {
        var c = await SeedClassAsync();
        (await InsertHoldFailureAsync(NewHold(c.Id, expiresAt: Now))).Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Confirmed_hold_requires_confirmed_at()
    {
        var c = await SeedClassAsync();
        var hold = NewHold(c.Id, status: HoldStatus.Confirmed);
        hold.ConfirmedAt = null;
        (await InsertHoldFailureAsync(hold)).Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Student_cannot_have_two_active_held_applications_across_classes()
    {
        var a = await SeedClassAsync();
        var b = await SeedClassAsync();
        var student = NewStudentId();
        await using (var db = pg.CreateContext()) { db.Holds.Add(NewHold(a.Id, student)); await db.SaveChangesAsync(); }
        (await InsertHoldFailureAsync(NewHold(b.Id, student))).Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Theory]
    [InlineData(HoldStatus.Held)]
    [InlineData(HoldStatus.Confirmed)]
    [InlineData(HoldStatus.Cancelled)]
    public async Task Student_cannot_reapply_to_same_class_unless_previous_expired(HoldStatus previous)
    {
        var c = await SeedClassAsync();
        var student = NewStudentId();
        await using (var db = pg.CreateContext()) { db.Holds.Add(NewHold(c.Id, student, previous)); await db.SaveChangesAsync(); }
        (await InsertHoldFailureAsync(NewHold(c.Id, student))).Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Student_can_reapply_to_same_class_after_expiry()
    {
        var c = await SeedClassAsync();
        var student = NewStudentId();
        await using (var db = pg.CreateContext()) { db.Holds.Add(NewHold(c.Id, student, HoldStatus.Expired)); await db.SaveChangesAsync(); }
        await using var db2 = pg.CreateContext();
        db2.Holds.Add(NewHold(c.Id, student));
        await db2.Invoking(d => d.SaveChangesAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Capacity_trigger_rejects_insert_beyond_total_seats()
    {
        var c = await SeedClassAsync(seats: 2);
        await using (var db = pg.CreateContext())
        {
            db.Holds.Add(NewHold(c.Id));
            db.Holds.Add(NewHold(c.Id, status: HoldStatus.Confirmed));
            await db.SaveChangesAsync();
        }
        (await InsertHoldFailureAsync(NewHold(c.Id))).Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Expired_holds_do_not_consume_capacity()
    {
        var c = await SeedClassAsync(seats: 1);
        await using (var db = pg.CreateContext())
        {
            db.Holds.Add(NewHold(c.Id, status: HoldStatus.Expired));
            db.Holds.Add(NewHold(c.Id, status: HoldStatus.Cancelled));
            await db.SaveChangesAsync();
        }
        await using var db2 = pg.CreateContext();
        db2.Holds.Add(NewHold(c.Id));
        await db2.Invoking(d => d.SaveChangesAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Unswept_but_time_expired_hold_does_not_consume_capacity()
    {
        var c = await SeedClassAsync(seats: 1);
        await using (var db = pg.CreateContext())
        {
            // Status still HELD but expired before the new hold is created (sweeper has not run yet).
            var stale = NewHold(c.Id);
            stale.CreatedAt = Now.AddHours(-1);
            stale.ExpiresAt = Now.AddMinutes(-50);
            db.Holds.Add(stale);
            await db.SaveChangesAsync();
        }
        await using var db2 = pg.CreateContext();
        db2.Holds.Add(NewHold(c.Id));
        await db2.Invoking(d => d.SaveChangesAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Confirming_a_held_hold_in_a_full_class_is_allowed()
    {
        var c = await SeedClassAsync(seats: 1);
        var hold = NewHold(c.Id);
        await using (var db = pg.CreateContext()) { db.Holds.Add(hold); await db.SaveChangesAsync(); }

        await using var db2 = pg.CreateContext();
        var h = await db2.Holds.SingleAsync(x => x.Id == hold.Id);
        h.Status = HoldStatus.Confirmed;
        h.ConfirmedAt = Now.AddMinutes(1);
        await db2.Invoking(d => d.SaveChangesAsync()).Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("admission_holds", "ix_admission_holds_class_id")]
    [InlineData("admission_holds", "ix_admission_holds_student_id")]
    [InlineData("admission_holds", "ix_admission_holds_status")]
    [InlineData("admission_holds", "ix_admission_holds_expires_at")]
    [InlineData("admission_holds", "ux_admission_holds_one_held_per_student")]
    [InlineData("admission_holds", "ux_admission_holds_one_application_per_class")]
    [InlineData("admission_classes", "ux_admission_classes_name_lower")]
    public async Task Required_indexes_exist(string table, string index)
    {
        await using var db = pg.CreateContext();
        var count = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE tablename = {table} AND indexname = {index}")
            .SingleAsync();
        count.Should().Be(1);
    }
}
