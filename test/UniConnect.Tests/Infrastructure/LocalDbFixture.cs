using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// A real SQL Server LocalDB database, created fresh and dropped afterwards.
///
/// Everything else in this suite runs on SQLite because it's fast and needs no
/// server. The one thing SQLite cannot do is optimistic concurrency: the
/// [Timestamp] tokens on Ride and StudyGroup are rowversion columns that the
/// *server* increments on every update, and SQLite has no equivalent — the
/// column exists but stays null, so a concurrency check never fires and a test
/// for it would pass whether or not the protection worked.
///
/// Only the two documented "simultaneous" edge cases live here. Skipped
/// automatically if LocalDB isn't installed, rather than failing the build on a
/// machine that doesn't have SQL Server.
/// </summary>
public sealed class LocalDbFixture : IDisposable
{
    private const string Master =
        @"Server=(localdb)\mssqllocaldb;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly string _databaseName = "UniConnectTests_" + Guid.NewGuid().ToString("N")[..12];

    public bool Available { get; }
    public string? SkipReason { get; }

    public LocalDbFixture()
    {
        try
        {
            using var probe = new SqlConnection(Master);
            probe.Open();

            using var context = NewContext();
            context.Database.EnsureCreated();

            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = $"SQL Server LocalDB is not available on this machine ({ex.GetType().Name}).";
        }
    }

    private string ConnectionString =>
        $@"Server=(localdb)\mssqllocaldb;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";

    public ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    public void Dispose()
    {
        if (!Available) return;

        try
        {
            using var context = NewContext();
            context.Database.EnsureDeleted();
        }
        catch
        {
            // A leftover test database is untidy, not a failure — never let
            // cleanup turn a green run red.
        }
    }
}

[CollectionDefinition(Name)]
public class LocalDbCollection : ICollectionFixture<LocalDbFixture>
{
    public const string Name = "LocalDb";
}
