using Microsoft.Data.Sqlite;
using Stx.Core.Logging;
using Stx.Core.Queue;

namespace Stx.Data;

public sealed class MediaRepo(SqliteConnection connection) : IMediaCatalog
{
    /// <summary>Tokens are scrubbed from the link here, at the only door into the table.</summary>
    public long AddItem(string? pageUrl, string? sourceId, string? title)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO media_items (page_url, source_id, title) VALUES ($url, $source, $title) RETURNING id;";
            command.Parameters.AddWithValue("$url", (object?)(pageUrl is null ? null : Redactor.ScrubLink(pageUrl)) ?? DBNull.Value);
            command.Parameters.AddWithValue("$source", (object?)sourceId ?? DBNull.Value);
            command.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    public MediaRecord? GetItem(long id)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, page_url, source_id, title FROM media_items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new MediaRecord(reader.GetInt64(0), Text(reader, 1), Text(reader, 2), Text(reader, 3));
        }
    }

    public void DeleteItem(long id)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM media_items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public long AddFile(long mediaId, string path, string? hash, string format)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO media_files (media_id, path, hash, format) VALUES ($media, $path, $hash, $format) RETURNING id;";
            command.Parameters.AddWithValue("$media", mediaId);
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$hash", (object?)hash ?? DBNull.Value);
            command.Parameters.AddWithValue("$format", format);
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    /// <summary>What's already saved, for the duplicate check.</summary>
    public IReadOnlyList<MediaFact> ExistingFacts()
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT i.source_id, f.hash, f.format FROM media_files f JOIN media_items i ON i.id = f.media_id;";
            using var reader = command.ExecuteReader();
            var facts = new List<MediaFact>();
            while (reader.Read()) facts.Add(new MediaFact(Text(reader, 0), Text(reader, 1), reader.GetString(2)));
            return facts;
        }
    }

    static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
