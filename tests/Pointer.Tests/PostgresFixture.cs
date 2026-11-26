using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pointer.Core.Data;

namespace Pointer.Tests;

/// <summary>
/// Creates a throwaway PostgreSQL database, applies the real migrations to it and drops it afterwards.
/// Tests run against real Postgres because the guarantees under test (exclusion constraint,
/// composite foreign keys) only exist in the database.
/// Server: POINTER_TEST_DB, default Host=localhost;Username=postgres;Password=postgres.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _serverConnectionString =
        Environment.GetEnvironmentVariable("POINTER_TEST_DB")
        ?? "Host=localhost;Username=postgres;Password=postgres";

    private readonly string _databaseName = $"pointer_test_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = "";

    public PointerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PointerDbContext>().UsePointerDatabase(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        ConnectionString = new NpgsqlConnectionStringBuilder(_serverConnectionString) { Database = _databaseName }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_serverConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
