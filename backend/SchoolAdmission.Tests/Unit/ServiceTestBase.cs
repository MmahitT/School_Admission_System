using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SchoolAdmission.Application.Options;
using SchoolAdmission.Application.Services;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Tests.Support;

namespace SchoolAdmission.Tests.Unit;

public abstract class ServiceTestBase
{
    protected const string StudentA = "20152691234567890";
    protected const string StudentB = "20152691234567891";

    protected FakeAdmissionRepository Repo { get; } = new();
    protected FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
    protected ClassService Classes { get; }
    protected HoldService Holds { get; }

    protected ServiceTestBase(int holdSeconds = 600)
    {
        Classes = new ClassService(Repo, Clock);
        Holds = new HoldService(Repo, Repo, Clock,
            Microsoft.Extensions.Options.Options.Create(new AdmissionOptions { HoldDurationSeconds = holdSeconds }),
            NullLogger<HoldService>.Instance);
    }

    protected AdmissionClass SeedClass(int seats = 2, string name = "Class 6 - Morning Shift")
    {
        var c = new AdmissionClass
        {
            Id = Guid.NewGuid(), Name = name, TotalSeats = seats,
            CreatedAt = Clock.GetUtcNow().UtcDateTime, UpdatedAt = Clock.GetUtcNow().UtcDateTime,
        };
        Repo.Classes.Add(c);
        return c;
    }
}
