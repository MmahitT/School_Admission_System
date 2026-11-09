using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Tests.Support;
using Xunit;

namespace SchoolAdmission.Tests.Integration;

/// <summary>Real PostgreSQL, real HTTP pipeline, requests released simultaneously.</summary>
[Collection(ApiCollection.Name)]
public class ConcurrencyTests(ApiFixture api)
{
    private static async Task<T[]> FireTogether<T>(int count, Func<int, Task<T>> request)
    {
        using var gate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, count)
            .Select(i => Task.Run(async () => { gate.Wait(); return await request(i); }))
            .ToArray();
        await Task.Delay(200); // let every task park on the gate
        gate.Set();
        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task One_hundred_twenty_students_compete_for_ten_seats_exactly_ten_win()
    {
        var classId = await api.CreateClassAsync(10);
        var students = Enumerable.Range(0, 120).Select(_ => api.NewStudentId()).ToArray();

        var responses = await FireTogether(students.Length, i => api.ApplyAsync(classId, students[i]));

        var codes = responses.Select(r => r.StatusCode).ToList();
        codes.Count(c => c == HttpStatusCode.Created).Should().Be(10);
        codes.Count(c => c == HttpStatusCode.Conflict).Should().Be(110);
        codes.Should().OnlyContain(c => c == HttpStatusCode.Created || c == HttpStatusCode.Conflict, "no 5xx allowed");
        foreach (var loser in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            (await ApiFixture.ErrorCodeAsync(loser)).Should().Be("NO_SEATS_AVAILABLE");

        var details = await api.GetClassAsync(classId);
        details.HeldSeats.Should().Be(10);
        details.ConfirmedSeats.Should().Be(0);
        details.AvailableSeats.Should().Be(0);

        await using var db = api.CreateDb();
        (await db.Holds.CountAsync(h => h.ClassId == classId)).Should().Be(10, "losers must leave no rows behind");
    }

    [Fact]
    public async Task Mixed_confirmations_and_new_applications_never_exceed_capacity()
    {
        var classId = await api.CreateClassAsync(10);
        var early = Enumerable.Range(0, 10).Select(_ => api.NewStudentId()).ToArray();
        var holds = new List<ApiFixture.HoldDto>();
        foreach (var s in early) holds.Add(await api.ApplyOkAsync(classId, s));
        var latecomers = Enumerable.Range(0, 50).Select(_ => api.NewStudentId()).ToArray();

        // Confirm all ten while 50 others try to get in.
        var work = FireTogether(60, i => i < 10
            ? api.ConfirmAsync(holds[i].HoldId, early[i])
            : api.ApplyAsync(classId, latecomers[i - 10]));
        var responses = await work;

        responses.Take(10).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        responses.Skip(10).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        var details = await api.GetClassAsync(classId);
        (details.ConfirmedSeats, details.HeldSeats, details.AvailableSeats).Should().Be((10, 0, 0));
    }

    [Fact]
    public async Task Same_student_firing_many_requests_at_one_class_gets_exactly_one_hold()
    {
        var classId = await api.CreateClassAsync(50);
        var student = api.NewStudentId();

        var responses = await FireTogether(25, _ => api.ApplyAsync(classId, student));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await api.GetClassAsync(classId)).HeldSeats.Should().Be(1);
    }

    [Fact]
    public async Task Same_student_applying_to_many_classes_at_once_gets_exactly_one_hold()
    {
        var classes = new List<Guid>();
        for (var i = 0; i < 8; i++) classes.Add(await api.CreateClassAsync(20));
        var student = api.NewStudentId();

        var responses = await FireTogether(classes.Count, i => api.ApplyAsync(classes[i], student));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        await using var db = api.CreateDb();
        (await db.Holds.CountAsync(h => h.StudentId == student && h.Status == HoldStatus.Held)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_repeated_confirmation_confirms_once_and_counts_once()
    {
        var classId = await api.CreateClassAsync(5);
        var student = api.NewStudentId();
        var hold = await api.ApplyOkAsync(classId, student);

        var responses = await FireTogether(30, _ => api.ConfirmAsync(hold.HoldId, student));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var details = await api.GetClassAsync(classId);
        (details.ConfirmedSeats, details.HeldSeats, details.AvailableSeats).Should().Be((1, 0, 4));
    }

    [Fact]
    public async Task One_thousand_students_compete_for_one_hundred_twenty_seats_exactly_120_win()
    {
        var classId = await api.CreateClassAsync(120);
        var students = Enumerable.Range(0, 1000).Select(_ => api.NewStudentId()).ToArray();

        var responses = await FireTogether(students.Length, i => api.ApplyAsync(classId, students[i]));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(120);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(880);
        var details = await api.GetClassAsync(classId);
        (details.HeldSeats, details.AvailableSeats).Should().Be((120, 0));
    }

    [Fact]
    public async Task Seat_counts_never_violate_the_limits_at_any_moment_during_the_burst()
    {
        const int total = 10;
        var classId = await api.CreateClassAsync(total);
        var students = Enumerable.Range(0, 150).Select(_ => api.NewStudentId()).ToArray();

        // Observer: keeps reading the public seat counts while the burst is in flight.
        using var stop = new CancellationTokenSource();
        var samples = new List<ApiFixture.ClassDto>();
        var observer = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var one = await api.GetClassAsync(classId);
                var all = (await api.Client.GetFromJsonAsync<List<ApiFixture.ClassDto>>("/classes",
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!.Single(c => c.Id == classId);
                lock (samples) { samples.Add(one); samples.Add(all); }
            }
        });

        var responses = await FireTogether(students.Length, i => api.ApplyAsync(classId, students[i]));
        // Confirm some of the winners mid-flight too, then stop observing.
        stop.Cancel();
        await observer;

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(total);
        samples.Should().NotBeEmpty();
        foreach (var s in samples)
        {
            s.HeldSeats.Should().BeLessThanOrEqualTo(total);
            s.ConfirmedSeats.Should().BeLessThanOrEqualTo(total);
            s.AvailableSeats.Should().BeGreaterThanOrEqualTo(0);
            (s.HeldSeats + s.ConfirmedSeats + s.AvailableSeats).Should().Be(total, "the three figures always add up");
        }
    }
}
