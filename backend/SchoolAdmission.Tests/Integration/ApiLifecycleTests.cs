using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Tests.Support;
using Xunit;

namespace SchoolAdmission.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ApiLifecycleTests(ApiFixture api)
{
    private record MyHold(Guid HoldId, Guid ClassId, string ClassName, string Status, DateTime ExpiresAt);
    private record HoldView(Guid HoldId, Guid ClassId, string Status, DateTime ExpiresAt);
    private record Confirmed(Guid HoldId, string Status);

    // ---- classes ------------------------------------------------------------------------------------
    [Fact]
    public async Task Create_class_returns_201_with_location_and_persists()
    {
        var res = await api.Client.PostAsJsonAsync("/classes", new { name = "Class 6 - Morning Shift " + Guid.NewGuid().ToString("N"), totalSeats = 120 });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        res.Headers.Location.Should().NotBeNull();

        var created = await ApiFixture.ReadAsync<ApiFixture.ClassDto>(res);
        var fetched = await api.GetClassAsync(created.Id);
        fetched.TotalSeats.Should().Be(120);
        fetched.AvailableSeats.Should().Be(120);

        await using var db = api.CreateDb();
        (await db.Classes.AnyAsync(c => c.Id == created.Id)).Should().BeTrue();
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("Valid", 0)]
    [InlineData("Valid", -1)]
    public async Task Create_class_rejects_invalid_input_with_400(string name, int seats)
    {
        var res = await api.Client.PostAsJsonAsync("/classes", new { name, totalSeats = seats });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiFixture.ErrorCodeAsync(res)).Should().Be("VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"name\":\"x\",\"totalSeats\":\"many\"}")]
    [InlineData("")]
    public async Task Create_class_rejects_malformed_body_with_400_not_500(string body)
    {
        var res = await api.Client.PostAsync("/classes", new StringContent(body, Encoding.UTF8, "application/json"));
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Duplicate_class_name_is_409_case_insensitive()
    {
        var name = "Dup " + Guid.NewGuid().ToString("N");
        await api.CreateClassAsync(5, name);
        var res = await api.Client.PostAsJsonAsync("/classes", new { name = name.ToUpperInvariant(), totalSeats = 5 });
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiFixture.ErrorCodeAsync(res)).Should().Be("DUPLICATE_CLASS_NAME");
    }

    [Fact]
    public async Task List_classes_returns_seat_information()
    {
        var id = await api.CreateClassAsync(4);
        var list = await api.Client.GetFromJsonAsync<List<ApiFixture.ClassDto>>("/classes", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        list.Should().Contain(c => c.Id == id && c.TotalSeats == 4 && c.AvailableSeats == 4);
    }

    [Fact]
    public async Task Unknown_class_is_404_and_bad_guid_is_400()
    {
        (await api.Client.GetAsync($"/classes/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await api.Client.GetAsync("/classes/not-a-guid")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await api.ApplyAsync(Guid.NewGuid(), api.NewStudentId())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- holds --------------------------------------------------------------------------------------
    [Fact]
    public async Task Full_lifecycle_apply_confirm_and_idempotent_reconfirm()
    {
        var classId = await api.CreateClassAsync(5);
        var student = api.NewStudentId();

        var hold = await api.ApplyOkAsync(classId, student);
        hold.Status.Should().Be("HELD");
        hold.StudentId.Should().Be(student);
        hold.ExpiresAt.Should().Be(api.Clock.GetUtcNow().UtcDateTime.AddSeconds(ApiFixture.HoldSeconds));
        (await api.GetClassAsync(classId)).Should().Match<ApiFixture.ClassDto>(c => c.HeldSeats == 1 && c.AvailableSeats == 4);

        var view = await api.Client.GetFromJsonAsync<HoldView>($"/holds/{hold.HoldId}");
        view!.Status.Should().Be("HELD");

        var confirm1 = await api.ConfirmAsync(hold.HoldId, student);
        confirm1.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ApiFixture.ReadAsync<Confirmed>(confirm1)).Status.Should().Be("CONFIRMED");

        DateTime? confirmedAt;
        await using (var db = api.CreateDb()) confirmedAt = (await db.Holds.SingleAsync(h => h.Id == hold.HoldId)).ConfirmedAt;
        confirmedAt.Should().NotBeNull();

        api.Clock.Advance(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 3; i++)
            (await api.ConfirmAsync(hold.HoldId, student)).StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await api.GetClassAsync(classId);
        after.ConfirmedSeats.Should().Be(1);
        after.HeldSeats.Should().Be(0);
        after.AvailableSeats.Should().Be(4);
        await using (var db = api.CreateDb())
            (await db.Holds.SingleAsync(h => h.Id == hold.HoldId)).ConfirmedAt.Should().Be(confirmedAt, "repeats must not rewrite ConfirmedAt");
    }

    [Fact]
    public async Task Missing_or_invalid_student_header_is_400()
    {
        var classId = await api.CreateClassAsync(1);
        foreach (var bad in new string?[] { null, "123", "2015269123456789a", "201526912345678901" })
        {
            var res = await api.ApplyAsync(classId, bad);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ApiFixture.ErrorCodeAsync(res)).Should().Be("INVALID_STUDENT_ID");
        }
        (await api.MyHoldsAsync(null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await api.GetClassAsync(classId)).AvailableSeats.Should().Be(1);
    }

    [Fact]
    public async Task Error_bodies_use_the_documented_shape_and_user_messages()
    {
        var classId = await api.CreateClassAsync(1);
        await api.ApplyOkAsync(classId, api.NewStudentId());
        var res = await api.ApplyAsync(classId, api.NewStudentId());
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("\"code\":\"NO_SEATS_AVAILABLE\"")
            .And.Contain("No seats are currently available for this class.")
            .And.Contain("traceId")
            .And.NotContain("Npgsql").And.NotContain("   at ");
    }

    [Fact]
    public async Task Only_the_owner_can_confirm_or_cancel()
    {
        var classId = await api.CreateClassAsync(2);
        var owner = api.NewStudentId();
        var hold = await api.ApplyOkAsync(classId, owner);
        var intruder = api.NewStudentId();

        (await api.ConfirmAsync(hold.HoldId, intruder)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.CancelAsync(hold.HoldId, intruder)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.ConfirmAsync(Guid.NewGuid(), owner)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = api.CreateDb();
        (await db.Holds.AsNoTracking().SingleAsync(h => h.Id == hold.HoldId)).Status.Should().Be(HoldStatus.Held);
    }

    [Fact]
    public async Task Conflicts_active_hold_and_already_applied()
    {
        var a = await api.CreateClassAsync(5);
        var b = await api.CreateClassAsync(5);
        var student = api.NewStudentId();

        var hold = await api.ApplyOkAsync(a, student);
        var second = await api.ApplyAsync(b, student);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiFixture.ErrorCodeAsync(second)).Should().Be("ACTIVE_HOLD_EXISTS");

        (await api.CancelAsync(hold.HoldId, student)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var again = await api.ApplyAsync(a, student);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiFixture.ErrorCodeAsync(again)).Should().Be("ALREADY_APPLIED");

        (await api.ApplyAsync(b, student)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cancel_releases_seat_is_idempotent_and_confirmed_cannot_be_cancelled()
    {
        var classId = await api.CreateClassAsync(1);
        var s1 = api.NewStudentId(); var s2 = api.NewStudentId();
        var h1 = await api.ApplyOkAsync(classId, s1);
        (await api.ApplyAsync(classId, s2)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await api.CancelAsync(h1.HoldId, s1)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await api.CancelAsync(h1.HoldId, s1)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await api.GetClassAsync(classId)).AvailableSeats.Should().Be(1);

        var h2 = await api.ApplyOkAsync(classId, s2);
        (await api.ConfirmAsync(h2.HoldId, s2)).StatusCode.Should().Be(HttpStatusCode.OK);
        var res = await api.CancelAsync(h2.HoldId, s2);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiFixture.ErrorCodeAsync(res)).Should().Be("HOLD_CONFIRMED");
        (await api.ConfirmAsync(h1.HoldId, s1)).StatusCode.Should().Be(HttpStatusCode.Conflict); // cancelled
    }

    [Fact]
    public async Task Expired_hold_frees_the_seat_blocks_confirmation_and_allows_reapplying()
    {
        var classId = await api.CreateClassAsync(1);
        var s1 = api.NewStudentId(); var s2 = api.NewStudentId();
        var hold = await api.ApplyOkAsync(classId, s1);
        (await api.ApplyAsync(classId, s2)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        api.Clock.Advance(TimeSpan.FromSeconds(ApiFixture.HoldSeconds)); // the sweeper has NOT run

        // Stored status is still HELD, yet every API already treats it as expired.
        await using (var db = api.CreateDb())
            (await db.Holds.AsNoTracking().SingleAsync(h => h.Id == hold.HoldId)).Status.Should().Be(HoldStatus.Held);
        (await api.Client.GetFromJsonAsync<HoldView>($"/holds/{hold.HoldId}"))!.Status.Should().Be("EXPIRED");
        var c = await api.GetClassAsync(classId);
        (c.HeldSeats, c.AvailableSeats).Should().Be((0, 1));

        var confirm = await api.ConfirmAsync(hold.HoldId, s1);
        confirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ApiFixture.ErrorCodeAsync(confirm)).Should().Be("HOLD_EXPIRED");
        await using (var db = api.CreateDb())
            (await db.Holds.AsNoTracking().SingleAsync(h => h.Id == hold.HoldId)).Status.Should().Be(HoldStatus.Expired);

        await api.ApplyOkAsync(classId, s2);             // another student takes the seat
        var s1Again = await api.ApplyAsync(classId, s1); // s1 may re-apply, but the class is full again
        (await ApiFixture.ErrorCodeAsync(s1Again)).Should().Be("NO_SEATS_AVAILABLE");
    }

    [Fact]
    public async Task Unswept_expired_hold_does_not_block_the_same_student_applying_again()
    {
        var classId = await api.CreateClassAsync(3);
        var student = api.NewStudentId();
        await api.ApplyOkAsync(classId, student);
        api.Clock.Advance(TimeSpan.FromSeconds(ApiFixture.HoldSeconds + 1));

        var again = await api.ApplyOkAsync(classId, student); // lazy expiry avoids the unique-index trap
        again.Status.Should().Be("HELD");
    }

    // ---- /me/holds ----------------------------------------------------------------------------------
    [Fact]
    public async Task My_holds_returns_only_own_records_with_class_names_and_updated_statuses()
    {
        var name = "Mine " + Guid.NewGuid().ToString("N");
        var classId = await api.CreateClassAsync(3, name);
        var other = await api.CreateClassAsync(3);
        var me = api.NewStudentId(); var someoneElse = api.NewStudentId();
        var hold = await api.ApplyOkAsync(classId, me);
        await api.ApplyOkAsync(other, someoneElse);

        var mine = await ApiFixture.ReadAsync<List<MyHold>>(await api.MyHoldsAsync(me));
        mine.Should().ContainSingle().Which.Should().Match<MyHold>(h =>
            h.HoldId == hold.HoldId && h.ClassName == name && h.Status == "HELD" && h.ClassId == classId);

        api.Clock.Advance(TimeSpan.FromSeconds(ApiFixture.HoldSeconds + 1));
        (await ApiFixture.ReadAsync<List<MyHold>>(await api.MyHoldsAsync(me))).Single().Status.Should().Be("EXPIRED");
        await using var db = api.CreateDb();
        (await db.Holds.AsNoTracking().SingleAsync(h => h.Id == hold.HoldId)).Status.Should().Be(HoldStatus.Expired);

        (await ApiFixture.ReadAsync<List<MyHold>>(await api.MyHoldsAsync(api.NewStudentId()))).Should().BeEmpty();
    }
}
