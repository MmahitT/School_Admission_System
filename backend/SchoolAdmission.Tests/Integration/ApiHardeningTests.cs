using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Application.Contracts;
using SchoolAdmission.Application.Services;
using SchoolAdmission.Tests.Support;
using Xunit;

namespace SchoolAdmission.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ApiHardeningTests(ApiFixture api)
{
    private sealed class ExplodingClassService : IClassService
    {
        public Task<ClassResponse> CreateAsync(CreateClassRequest r, CancellationToken ct) => throw new InvalidOperationException("secret-connection-string password=hunter2");
        public Task<IReadOnlyList<ClassDetailsResponse>> ListAsync(CancellationToken ct) => throw new InvalidOperationException("secret-connection-string password=hunter2");
        public Task<ClassDetailsResponse> GetAsync(Guid id, CancellationToken ct) => throw new InvalidOperationException("secret-connection-string password=hunter2");
    }

    [Fact]
    public async Task Unexpected_exceptions_become_a_generic_500_without_leaking_internals()
    {
        using var client = api.CreateClient(b => b.ConfigureServices(s => s.AddScoped<IClassService, ExplodingClassService>()));
        var res = await client.GetAsync("/classes");

        res.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("INTERNAL_ERROR").And.Contain("Something went wrong. Please try again.").And.Contain("traceId");
        body.Should().NotContain("secret").And.NotContain("hunter2").And.NotContain("InvalidOperationException").And.NotContain("   at ");
    }

    [Fact]
    public async Task Unknown_route_is_404_and_wrong_method_is_405_not_500()
    {
        (await api.Client.GetAsync("/nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await api.Client.PutAsJsonAsync("/classes", new { })).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await api.Client.DeleteAsync("/classes")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Health_endpoint_reports_ok()
    {
        var res = await api.Client.GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("ok");
    }

    [Fact]
    public async Task Student_id_header_with_unicode_digits_inner_spaces_or_wrong_length_is_rejected()
    {
        var classId = await api.CreateClassAsync(1);
        var sent = 0;
        // Leading/trailing spaces are not tested: HTTP itself strips them from header values.
        foreach (var bad in new[] { "2015269123456789٠" /* arabic-indic zero, 17 chars */, "2015269123456789a", "2015 2691234567890", "20152691234567890123" })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
            req.Headers.TryAddWithoutValidation("X-Student-Id", bad);
            var res = await api.Client.SendAsync(req);
            sent++;
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"'{bad}' is not 17 ASCII digits");
        }
        sent.Should().Be(4);
        (await api.GetClassAsync(classId)).HeldSeats.Should().Be(0);
    }

    [Fact]
    public async Task Sql_injection_attempts_are_treated_as_plain_invalid_input()
    {
        var classId = await api.CreateClassAsync(1);
        var res = await api.ApplyAsync(classId, "1'; DROP TABLE admission_holds;--");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var name = "x'); DROP TABLE admission_classes;-- " + Guid.NewGuid().ToString("N");
        var created = await api.Client.PostAsJsonAsync("/classes", new { name, totalSeats = 1 });
        created.StatusCode.Should().Be(HttpStatusCode.Created, "the text is stored literally, never executed");
        (await api.Client.GetAsync("/classes")).StatusCode.Should().Be(HttpStatusCode.OK); // tables still exist
    }

    [Fact]
    public async Task Cors_allows_only_configured_origins()
    {
        using var client = api.CreateClient(b => b.UseSetting("CORS_ALLOWED_ORIGINS", "http://localhost:5173"));

        var allowed = new HttpRequestMessage(HttpMethod.Get, "/classes");
        allowed.Headers.Add("Origin", "http://localhost:5173");
        (await client.SendAsync(allowed)).Headers.TryGetValues("Access-Control-Allow-Origin", out var origin).Should().BeTrue();
        origin!.Single().Should().Be("http://localhost:5173");

        var other = new HttpRequestMessage(HttpMethod.Get, "/classes");
        other.Headers.Add("Origin", "http://evil.example");
        (await client.SendAsync(other)).Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
