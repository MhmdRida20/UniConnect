using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// A throwaway relational database for one test.
///
/// SQLite rather than EF's InMemory provider, deliberately. A large share of
/// UniConnect's rules are enforced by unique indexes rather than by C# — a
/// student can't submit attendance twice, apply to the same internship twice,
/// or join a club twice — and InMemory enforces none of them. Tests for those
/// rules would pass against InMemory whether or not the constraint existed,
/// which is worse than having no test at all.
///
/// The connection is held open for the object's lifetime because an in-memory
/// SQLite database is destroyed the moment its last connection closes.
///
/// Schema comes from EnsureCreated, never Migrate: the migrations in this repo
/// are SQL Server-specific.
///
/// Known limitation, and the reason ConcurrencyTests live elsewhere: SQLite has
/// no server-generated rowversion, so the [Timestamp] tokens on Ride and
/// StudyGroup are never populated and optimistic-concurrency checks cannot fire
/// here. Those two cases run against LocalDB — see LocalDbFixture.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public ApplicationDbContext Db { get; }

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Db = new ApplicationDbContext(BuildOptions());
        Db.Database.EnsureCreated();
    }

    private DbContextOptions<ApplicationDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;

    /// <summary>
    /// A second context over the same database. Use when a test needs to prove
    /// something was actually persisted rather than just sitting in the first
    /// context's change tracker.
    /// </summary>
    public ApplicationDbContext NewContext() => new(BuildOptions());

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
