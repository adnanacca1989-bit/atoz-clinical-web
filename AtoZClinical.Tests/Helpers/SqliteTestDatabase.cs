using AtoZClinical.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests.Helpers;

public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    public ClinicalDbContext Db { get; }

    private SqliteTestDatabase(SqliteConnection connection, ClinicalDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public static async Task<SqliteTestDatabase> CreateAsync(TestClinicProvider tenant)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new ClinicalDbContext(options, tenant);
        await db.Database.EnsureCreatedAsync();

        return new SqliteTestDatabase(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
