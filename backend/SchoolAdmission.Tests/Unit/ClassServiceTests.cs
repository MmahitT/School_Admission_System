using FluentAssertions;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Application.Contracts;
using SchoolAdmission.Domain;
using SchoolAdmission.Domain.Entities;
using Xunit;

namespace SchoolAdmission.Tests.Unit;

public class ClassServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Valid_class_is_created_with_trimmed_name()
    {
        var result = await Classes.CreateAsync(new CreateClassRequest("  Class 6 - Morning Shift ", 120), default);
        result.Name.Should().Be("Class 6 - Morning Shift");
        result.TotalSeats.Should().Be(120);
        Repo.Classes.Should().ContainSingle().Which.Id.Should().Be(result.Id);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("Class 6", 0)]
    [InlineData("Class 6", -3)]
    [InlineData("Class 6", 100_001)]
    public async Task Invalid_class_is_rejected(string? name, int seats)
    {
        var act = () => Classes.CreateAsync(new CreateClassRequest(name, seats), default);
        (await act.Should().ThrowAsync<AdmissionException>()).Which.Code.Should().Be(ErrorCodes.ValidationFailed);
        Repo.Classes.Should().BeEmpty();
    }

    [Fact]
    public async Task Overlong_name_is_rejected()
    {
        var act = () => Classes.CreateAsync(new CreateClassRequest(new string('x', 201), 5), default);
        await act.Should().ThrowAsync<AdmissionException>();
    }

    [Theory]
    [InlineData("20152691234567890", true)]
    [InlineData("00000000000000000", true)]
    [InlineData("2015269123456789", false)]
    [InlineData("201526912345678901", false)]
    [InlineData("2015269123456789a", false)]
    [InlineData(" 20152691234567890", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Student_id_validation(string? value, bool expected) => StudentId.IsValid(value).Should().Be(expected);

    [Fact]
    public async Task Seat_counts_are_correct_and_never_negative()
    {
        var c = SeedClass(seats: 3);
        var now = Clock.GetUtcNow().UtcDateTime;
        Repo.Holds.Add(NewHold(c.Id, "20000000000000001", HoldStatus.Held, now.AddMinutes(5)));
        Repo.Holds.Add(NewHold(c.Id, "20000000000000002", HoldStatus.Held, now.AddMinutes(-1))); // time-expired
        Repo.Holds.Add(NewHold(c.Id, "20000000000000003", HoldStatus.Confirmed, now.AddMinutes(-9)));
        Repo.Holds.Add(NewHold(c.Id, "20000000000000004", HoldStatus.Cancelled, now.AddMinutes(5)));

        var details = await Classes.GetAsync(c.Id, default);
        details.HeldSeats.Should().Be(1);
        details.ConfirmedSeats.Should().Be(1);
        details.AvailableSeats.Should().Be(1);

        (await Classes.ListAsync(default)).Should().ContainSingle();
    }

    [Fact]
    public async Task Unknown_class_is_not_found()
    {
        var act = () => Classes.GetAsync(Guid.NewGuid(), default);
        (await act.Should().ThrowAsync<AdmissionException>()).Which.Kind.Should().Be(ErrorKind.NotFound);
    }

    private static AdmissionHold NewHold(Guid classId, string student, HoldStatus status, DateTime expires) => new()
    {
        Id = Guid.NewGuid(), ClassId = classId, StudentId = student, Status = status,
        CreatedAt = expires.AddMinutes(-10), ExpiresAt = expires,
    };
}
