using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SchoolAdmission.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace SchoolAdmission.Tests.Support;

/// <summary>Real API (WebApplicationFactory) on a real PostgreSQL container, with a controllable clock.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const int HoldSeconds = 600;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PostgresFixture _pg = new();
    private WebApplicationFactory<Program> _factory = null!;
    private long _studentCounter = 20_260_000_000_000;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero));
    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => _factory.Services;

    /// <summary>A client for the same app with extra configuration (e.g. a throwing service, CORS origins).</summary>
    public HttpClient CreateClient(Action<IWebHostBuilder> configure) => _factory.WithWebHostBuilder(configure).CreateClient();

    public async Task InitializeAsync()
    {
        await _pg.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:DefaultConnection", _pg.ConnectionString);
            b.UseSetting("HOLD_DURATION_SECONDS", HoldSeconds.ToString());
            // The sweeper is exercised explicitly in ExpirationTests; keep it off so timing tests are deterministic.
            b.UseSetting("DisableExpirationSweeper", "true");
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock)));
        });
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _pg.DisposeAsync();
    }

    public string NewStudentId() => Interlocked.Increment(ref _studentCounter).ToString("D17");
    public AdmissionDbContext CreateDb() => _pg.CreateContext();

    // ---- HTTP helpers -------------------------------------------------------------------------------
    public record ClassDto(Guid Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats);
    public record HoldDto(Guid HoldId, Guid ClassId, string StudentId, string Status, DateTime ExpiresAt);

    public async Task<Guid> CreateClassAsync(int seats, string? name = null)
    {
        var res = await Client.PostAsJsonAsync("/classes", new { name = name ?? $"Class {Guid.NewGuid():N}", totalSeats = seats });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ClassDto>(Json))!.Id;
    }

    public async Task<ClassDto> GetClassAsync(Guid id) =>
        (await Client.GetFromJsonAsync<ClassDto>($"/classes/{id}", Json))!;

    public Task<HttpResponseMessage> ApplyAsync(Guid classId, string? student) => Send(HttpMethod.Post, $"/classes/{classId}/holds", student);
    public Task<HttpResponseMessage> ConfirmAsync(Guid holdId, string? student) => Send(HttpMethod.Post, $"/holds/{holdId}/confirm", student);
    public Task<HttpResponseMessage> CancelAsync(Guid holdId, string? student) => Send(HttpMethod.Delete, $"/holds/{holdId}", student);
    public Task<HttpResponseMessage> MyHoldsAsync(string? student) => Send(HttpMethod.Get, "/me/holds", student);

    public async Task<HoldDto> ApplyOkAsync(Guid classId, string student)
    {
        var res = await ApplyAsync(classId, student);
        res.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<HoldDto>(Json))!;
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string> ErrorCodeAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, string? student)
    {
        var req = new HttpRequestMessage(method, url);
        if (student is not null) req.Headers.Add("X-Student-Id", student);
        return Client.SendAsync(req);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
