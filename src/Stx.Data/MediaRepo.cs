using Microsoft.Data.Sqlite;
using Stx.Core.Logging;
using Stx.Core.Queue;

namespace Stx.Data;

public sealed class MediaRepo(SqliteConnection connection) : IMediaCatalog, ILibraryCatalog, IFactsStore
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

    public IReadOnlyList<LibraryItem> Library()
    {
        lock (connection)
        {
            var files = new Dictionary<long, List<LibraryFile>>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, media_id, path, format FROM media_files ORDER BY id;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long media = reader.GetInt64(1);
                    if (!files.TryGetValue(media, out var list)) files[media] = list = [];
                    list.Add(new LibraryFile(reader.GetInt64(0), reader.GetString(2), reader.GetString(3)));
                }
            }

            var items = new List<LibraryItem>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title, page_url FROM media_items ORDER BY id DESC;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    if (files.TryGetValue(id, out var list)) items.Add(new LibraryItem(id, Text(reader, 1), Text(reader, 2), list));
                }
            }
            return items;
        }
    }

    public string? FilePath(long fileId)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT path FROM media_files WHERE id = $id;";
            command.Parameters.AddWithValue("$id", fileId);
            return command.ExecuteScalar() as string;
        }
    }

    public void SaveFacts(long fileId, string json)
    {
        lock (connection)
        {
            using var transaction = connection.BeginTransaction();
            using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM audio_facts WHERE media_file_id = $id;";
                clear.Parameters.AddWithValue("$id", fileId);
                clear.ExecuteNonQuery();
            }
            using (var add = connection.CreateCommand())
            {
                add.Transaction = transaction;
                add.CommandText = "INSERT INTO audio_facts (media_file_id, facts_json) VALUES ($id, $json);";
                add.Parameters.AddWithValue("$id", fileId);
                add.Parameters.AddWithValue("$json", json);
                add.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public string? LoadFacts(long fileId)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT facts_json FROM audio_facts WHERE media_file_id = $id;";
            command.Parameters.AddWithValue("$id", fileId);
            return command.ExecuteScalar() as string;
        }
    }

    static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
