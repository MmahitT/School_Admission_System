using FluentAssertions;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Domain.Entities;
using Xunit;

namespace SchoolAdmission.Tests.Unit;

public class HoldServiceTests : ServiceTestBase
{
    private async Task<AdmissionException> Fails(Func<Task> act) => (await act.Should().ThrowAsync<AdmissionException>()).Which;

    [Fact]
    public async Task Successful_reservation_creates_held_hold_with_configured_expiry()
    {
        var c = SeedClass();
        var r = await Holds.CreateAsync(c.Id, StudentA, default);

        r.Status.Should().Be("HELD");
        r.StudentId.Should().Be(StudentA);
        r.ClassId.Should().Be(c.Id);
        r.ExpiresAt.Should().Be(Clock.GetUtcNow().UtcDateTime.AddSeconds(600));
        (await Classes.GetAsync(c.Id, default)).HeldSeats.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("123")]
    [InlineData("2015269123456789x")]
    public async Task Invalid_student_id_is_rejected_everywhere(string? id)
    {
        var c = SeedClass();
        (await Fails(() => Holds.CreateAsync(c.Id, id, default))).Code.Should().Be(ErrorCodes.InvalidStudentId);
        (await Fails(() => Holds.ConfirmAsync(Guid.NewGuid(), id, default))).Code.Should().Be(ErrorCodes.InvalidStudentId);
        (await Fails(() => Holds.CancelAsync(Guid.NewGuid(), id, default))).Code.Should().Be(ErrorCodes.InvalidStudentId);
        (await Fails(() => Holds.ListForStudentAsync(id, default))).Code.Should().Be(ErrorCodes.InvalidStudentId);
    }

    [Fact]
    public async Task Unknown_class_is_not_found()
    {
        (await Fails(() => Holds.CreateAsync(Guid.NewGuid(), StudentA, default))).Code.Should().Be(ErrorCodes.ClassNotFound);
    }

    [Fact]
    public async Task No_seats_remaining_is_a_conflict()
    {
        var c = SeedClass(seats: 1);
        await Holds.CreateAsync(c.Id, StudentA, default);
        var ex = await Fails(() => Holds.CreateAsync(c.Id, StudentB, default));
        ex.Code.Should().Be(ErrorCodes.NoSeatsAvailable);
        ex.Kind.Should().Be(ErrorKind.Conflict);
        Repo.Holds.Should().HaveCount(1);
    }

    [Fact]
    public async Task Student_with_active_hold_cannot_hold_another_class()
    {
        var a = SeedClass(name: "A"); var b = SeedClass(name: "B");
        await Holds.CreateAsync(a.Id, StudentA, default);
        (await Fails(() => Holds.CreateAsync(b.Id, StudentA, default))).Code.Should().Be(ErrorCodes.ActiveHoldExists);
    }

    [Fact]
    public async Task Student_cannot_reapply_to_same_class_after_cancellation_or_confirmation()
    {
        var a = SeedClass(name: "A");
        var hold = await Holds.CreateAsync(a.Id, StudentA, default);
        await Holds.CancelAsync(hold.HoldId, StudentA, default);
        (await Fails(() => Holds.CreateAsync(a.Id, StudentA, default))).Code.Should().Be(ErrorCodes.AlreadyApplied);

        var b = SeedClass(name: "B");
        var second = await Holds.CreateAsync(b.Id, StudentA, default);
        await Holds.ConfirmAsync(second.HoldId, StudentA, default);
        (await Fails(() => Holds.CreateAsync(b.Id, StudentA, default))).Code.Should().Be(ErrorCodes.AlreadyApplied);
    }

    [Fact]
    public async Task Student_can_reapply_after_cancelling_to_a_different_class()
    {
        var a = SeedClass(name: "A"); var b = SeedClass(name: "B");
        var hold = await Holds.CreateAsync(a.Id, StudentA, default);
        await Holds.CancelAsync(hold.HoldId, StudentA, default);
        var again = await Holds.CreateAsync(b.Id, StudentA, default);
        again.ClassId.Should().Be(b.Id);
    }

    [Fact]
    public async Task Confirmation_succeeds_and_is_idempotent()
    {
        var c = SeedClass(seats: 2);
        var hold = await Holds.CreateAsync(c.Id, StudentA, default);

        (await Holds.ConfirmAsync(hold.HoldId, StudentA, default)).Status.Should().Be("CONFIRMED");
        var stored = Repo.Holds.Single();
        var confirmedAt = stored.ConfirmedAt;
        confirmedAt.Should().NotBeNull();

        Clock.Advance(TimeSpan.FromSeconds(30));
        (await Holds.ConfirmAsync(hold.HoldId, StudentA, default)).Status.Should().Be("CONFIRMED");
        stored.ConfirmedAt.Should().Be(confirmedAt, "a repeat must not change anything");

        var details = await Classes.GetAsync(c.Id, default);
        details.ConfirmedSeats.Should().Be(1);
        details.HeldSeats.Should().Be(0);
    }

    [Fact]
    public async Task Confirmation_of_expired_hold_is_rejected_and_hold_marked_expired()
    {
        var c = SeedClass(seats: 1);
        var hold = await Holds.CreateAsync(c.Id, StudentA, default);
        Clock.Advance(TimeSpan.FromSeconds(600)); // exactly at expiry counts as expired

        (await Fails(() => Holds.ConfirmAsync(hold.HoldId, StudentA, default))).Code.Should().Be(ErrorCodes.HoldExpired);
        Repo.Holds.Single().Status.Should().Be(HoldStatus.Expired);
        (await Classes.GetAsync(c.Id, default)).AvailableSeats.Should().Be(1);
    }

    [Fact]
    public async Task Confirmation_of_cancelled_hold_is_rejected()
    {
        var c = SeedClass();
        var hold = await Holds.CreateAsync(c.Id, StudentA, default);
        await Holds.CancelAsync(hold.HoldId, StudentA, default);
        (await Fails(() => Holds.ConfirmAsync(hold.HoldId, StudentA, default))).Code.Should().Be(ErrorCodes.HoldCancelled);
    }

    [Fact]
    public async Task Other_students_cannot_confirm_or_cancel()
    {
        var c = SeedClass();
        var hold = await Holds.CreateAsync(c.Id, StudentA, default);
        (await Fails(() => Holds.ConfirmAsync(hold.HoldId, StudentB, default))).Kind.Should().Be(ErrorKind.Forbidden);
        (await Fails(() => Holds.CancelAsync(hold.HoldId, StudentB, default))).Kind.Should().Be(ErrorKind.Forbidden);
        Repo.Holds.Single().Status.Should().Be(HoldStatus.Held);
    }

    [Fact]
    public async Task Cancellation_releases_the_seat_and_is_idempotent()
    {
        var c = SeedClass(seats: 1);
        var hold = await Holds.CreateAsync(c.Id, StudentA, default);
        await Holds.CancelAsync(hold.HoldId, StudentA, default);
        await Holds.CancelAsync(hold.HoldId, StudentA, default);

        Repo.Holds.Single().Status.Should().Be(HoldStatus.Cancelled);
        Repo.Holds.Single().CancelledAt.Should().NotBeNull();
        (await Classes.GetAsync(c.Id, default)).AvailableSeats.Should().Be(1);
        await Holds.CreateAsync(c.Id, StudentB, default); // seat is reusable
    }

    [Fact]
    public async Task Confirmed_admission_cannot_be_cancelled()
    {
        var c = SeedClass();
        var hold = await Holds.CreateAsync(c.Id, StudentA, default);
        await Holds.ConfirmAsync(hold.HoldId, StudentA, default);
        (await Fails(() => Holds.CancelAsync(hold.HoldId, StudentA, default))).Code.Should().Be(ErrorCodes.HoldConfirmed);
        Repo.Holds.Single().Status.Should().Be(HoldStatus.Confirmed);
    }

    [Fact]
    public async Task Expired_hold_frees_the_seat_and_reapplying_is_allowed()
    {
        var c = SeedClass(seats: 1);
        var first = await Holds.CreateAsync(c.Id, StudentA, default);
        Clock.Advance(TimeSpan.FromSeconds(601));

        (await Holds.GetAsync(first.HoldId, default)).Status.Should().Be("EXPIRED");
        (await Classes.GetAsync(c.Id, default)).AvailableSeats.Should().Be(1);

        var again = await Holds.CreateAsync(c.Id, StudentA, default); // same student, same class, after expiry
        again.Status.Should().Be("HELD");
        Repo.Holds.Should().Contain(h => h.Id == first.HoldId && h.Status == HoldStatus.Expired);
    }

    [Fact]
    public async Task Get_unknown_hold_is_not_found()
    {
        (await Fails(() => Holds.GetAsync(Guid.NewGuid(), default))).Code.Should().Be(ErrorCodes.HoldNotFound);
    }

    [Fact]
    public async Task Student_sees_only_own_applications_with_class_names_and_expiry_applied()
    {
        var a = SeedClass(name: "Class 6 - Morning Shift"); var b = SeedClass(name: "Class 7 - Day Shift");
        var mine = await Holds.CreateAsync(a.Id, StudentA, default);
        await Holds.CreateAsync(b.Id, StudentB, default);

        var list = await Holds.ListForStudentAsync(StudentA, default);
        list.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { HoldId = mine.HoldId, ClassName = "Class 6 - Morning Shift", Status = "HELD" });

        Clock.Advance(TimeSpan.FromHours(1));
        (await Holds.ListForStudentAsync(StudentA, default)).Single().Status.Should().Be("EXPIRED");
        (await Holds.ListForStudentAsync("20152699999999999", default)).Should().BeEmpty();
    }
}
