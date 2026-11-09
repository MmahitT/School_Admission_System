using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace SchoolAdmission.Tests.Support;

/// <summary>One real PostgreSQL container per test collection, migrated with the real EF migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public AdmissionDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AdmissionDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AdmissionDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
