using System.Globalization;
using Microsoft.Data.Sqlite;
using Stx.Core.Library;
using Stx.Core.Logging;
using Stx.Core.Queue;

namespace Stx.Data;

public sealed class MediaRepo(SqliteConnection connection) : IMediaCatalog, ILibraryCatalog, IFactsStore, ICollectionCatalog
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
            command.CommandText = "SELECT i.source_id, f.hash, f.format, f.path, i.id, f.id FROM media_files f JOIN media_items i ON i.id = f.media_id;";
            using var reader = command.ExecuteReader();
            var facts = new List<MediaFact>();
            while (reader.Read()) facts.Add(new MediaFact(Text(reader, 0), Text(reader, 1), reader.GetString(2), Text(reader, 3), reader.GetInt64(4), reader.GetInt64(5)));
            return facts;
        }
    }

    /// <summary>Remembers which item a capture went to. The key is a hash, so nothing in a stream's address is stored.</summary>
    public void RememberCapture(string key, long mediaId)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO capture_keys (key_hash, media_id) VALUES ($key, $media);";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$media", mediaId);
            command.ExecuteNonQuery();
        }
    }

    public long? RememberedCapture(string key)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT media_id FROM capture_keys WHERE key_hash = $key;";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() is long media ? media : null;
        }
    }

    /// <summary>Records what a file's bytes are, and where they live, which differs from where it was saved when they were already here.</summary>
    public void SetFileIdentity(long fileId, string hash, string path)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE media_files SET hash = $hash, path = $path WHERE id = $id;";
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$id", fileId);
            command.ExecuteNonQuery();
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
                command.CommandText = "SELECT id, title, page_url, origin FROM media_items ORDER BY id DESC;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    if (files.TryGetValue(id, out var list)) items.Add(new LibraryItem(id, Text(reader, 1), Text(reader, 2), list, OriginOf(reader.GetString(3))));
                }
            }
            return items;
        }
    }

    public string? FileHash(long fileId)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT hash FROM media_files WHERE id = $id;";
            command.Parameters.AddWithValue("$id", fileId);
            return command.ExecuteScalar() as string;
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

    public IReadOnlyList<IndexedFile> IndexedFiles()
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT f.id, f.media_id, f.path, f.size, f.modified, i.origin FROM media_files f JOIN media_items i ON i.id = f.media_id;";
            using var reader = command.ExecuteReader();
            var files = new List<IndexedFile>();
            while (reader.Read())
                files.Add(new IndexedFile(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    OriginOf(reader.GetString(5))));
            return files;
        }
    }

    public void AddLocal(IReadOnlyList<FoundFile> files, MediaOrigin origin)
    {
        if (files.Count == 0) return;
        lock (connection)
        {
            using var transaction = connection.BeginTransaction();
            using var item = connection.CreateCommand();
            item.Transaction = transaction;
            item.CommandText = "INSERT INTO media_items (page_url, source_id, title, origin) VALUES (NULL, NULL, $title, $origin) RETURNING id;";
            var title = item.Parameters.Add("$title", SqliteType.Text);
            item.Parameters.AddWithValue("$origin", OriginText(origin));
            using var file = connection.CreateCommand();
            file.Transaction = transaction;
            file.CommandText = "INSERT INTO media_files (media_id, path, hash, format, size, modified) VALUES ($media, $path, NULL, 'Original', $size, $modified);";
            // One row per file. The scan and the watcher can both find a file that's new to them (review 2026-11-07),
            // so whatever already has a row for the path, in any case, keeps it, and the file isn't added again. The
            // lookup sees rows this same transaction added, so a batch that names a file twice adds it once.
            using var known = connection.CreateCommand();
            known.Transaction = transaction;
            known.CommandText = "SELECT 1 FROM media_files WHERE path = $path COLLATE NOCASE LIMIT 1;";
            var knownPath = known.Parameters.Add("$path", SqliteType.Text);
            var media = file.Parameters.Add("$media", SqliteType.Integer);
            var path = file.Parameters.Add("$path", SqliteType.Text);
            var size = file.Parameters.Add("$size", SqliteType.Integer);
            var modified = file.Parameters.Add("$modified", SqliteType.Text);
            foreach (var found in files)
            {
                knownPath.Value = found.Path;
                if (known.ExecuteScalar() is not null) continue;
                title.Value = Path.GetFileNameWithoutExtension(found.Path);
                media.Value = Convert.ToInt64(item.ExecuteScalar());
                path.Value = found.Path;
                size.Value = found.Size;
                modified.Value = Stamp(found.Modified);
                file.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public void MoveFile(long fileId, string newPath)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE media_files SET path = $path WHERE id = $id;" +
                "UPDATE media_items SET title = $title WHERE id = (SELECT media_id FROM media_files WHERE id = $id) AND origin <> 'downloaded';";
            command.Parameters.AddWithValue("$path", newPath);
            command.Parameters.AddWithValue("$title", Path.GetFileNameWithoutExtension(newPath));
            command.Parameters.AddWithValue("$id", fileId);
            command.ExecuteNonQuery();
        }
    }

    public void UpdateFacts(long fileId, long size, DateTimeOffset modified)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE media_files SET size = $size, modified = $modified WHERE id = $id;";
            command.Parameters.AddWithValue("$size", size);
            command.Parameters.AddWithValue("$modified", Stamp(modified));
            command.Parameters.AddWithValue("$id", fileId);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Only rows go. The files stay on disk, and downloads and opened files under the folder stay listed.</summary>
    public int ForgetWatchedUnder(string folder)
    {
        lock (connection)
        {
            var doomed = IndexedFiles().Where(f => f.Origin == MediaOrigin.Watched && FolderScan.IsUnder(f.Path, folder)).Select(f => f.MediaId).Distinct().ToList();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM media_items WHERE id = $id AND origin = 'watched';";
            var id = command.Parameters.Add("$id", SqliteType.Integer);
            foreach (long media in doomed)
            {
                id.Value = media;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            return doomed.Count;
        }
    }

    public void ForgetFile(long fileId)
    {
        lock (connection)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$id", fileId);
            command.CommandText = "SELECT f.media_id FROM media_files f JOIN media_items i ON i.id = f.media_id WHERE f.id = $id AND i.origin <> 'downloaded';";
            if (command.ExecuteScalar() is not long media) return;
            command.Parameters.AddWithValue("$media", media);
            command.CommandText =
                "DELETE FROM media_files WHERE id = $id;" +
                "DELETE FROM media_items WHERE id = $media AND NOT EXISTS (SELECT 1 FROM media_files WHERE media_id = $media);";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    static string Stamp(DateTimeOffset at) => at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    static string OriginText(MediaOrigin origin) => origin.ToString().ToLowerInvariant();

    static MediaOrigin OriginOf(string text) => Enum.TryParse<MediaOrigin>(text, ignoreCase: true, out var origin) ? origin : MediaOrigin.Downloaded;

    static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
