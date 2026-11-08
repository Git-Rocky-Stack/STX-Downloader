using Microsoft.Data.Sqlite;

namespace Stx.Data;

public static class Db
{
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STXDownloader", "stx.db");

    /// <summary>Opens the database, creating its folder and bringing the schema up to date.</summary>
    public static SqliteConnection Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            connection.Open();
            Execute(connection, "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;");
            Migrations.Apply(connection);
            return connection;
        }
        catch
        {
            // A step that fails is rolled back by its transaction; the file isn't left open behind it.
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The .NET function the schema calls: stx_fold, which folds a path's or a title's case for its key, every letter
    /// and not only A to Z as SQLite's own NOCASE does (<see cref="Stx.Core.Queue.CaseKey"/>). The Library's triggers
    /// call it on every write, so every connection that writes the Library needs it. Adding it again replaces it.
    /// </summary>
    public static void AddFunctions(SqliteConnection connection) =>
        connection.CreateFunction<string?, string?>("stx_fold", Stx.Core.Queue.CaseKey.Of, isDeterministic: true);

    public static int Version(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static IReadOnlyList<string> Tables(SqliteConnection connection) =>
        Column(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;", 0);

    public static IReadOnlyList<string> Columns(SqliteConnection connection, string table)
    {
        if (!Tables(connection).Contains(table)) return [];
        return Column(connection, $"PRAGMA table_info(\"{table}\");", 1);
    }

    static List<string> Column(SqliteConnection connection, string sql, int index)
    {
        var values = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read()) values.Add(reader.GetString(index));
        return values;
    }

    static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
