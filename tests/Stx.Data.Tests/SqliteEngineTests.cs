using Microsoft.Data.Sqlite;
using Xunit;

namespace Stx.Data.Tests;

public sealed class SqliteEngineTests
{
    /// <summary>
    /// CVE-2025-6965 (GHSA-2m69-gcr7-jv3q): SQLite before 3.50.2 can corrupt memory when aggregate terms
    /// outnumber the available columns. This reads the engine actually loaded, not the package version.
    /// </summary>
    [Fact]
    public void The_bundled_sqlite_is_past_the_aggregate_memory_corruption_fix()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var version = Version.Parse((string)command.ExecuteScalar()!);

        Assert.True(version >= new Version(3, 50, 2), $"SQLite {version} is older than 3.50.2.");
    }
}
