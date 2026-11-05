using Microsoft.Data.Sqlite;

namespace Stx.Data;

/// <summary>Hand-written schema steps. A step that has run never changes; new columns get a new step. Secrets have no columns here.</summary>
public static class Migrations
{
    static readonly string[] Steps =
    [
        """
        CREATE TABLE media_items (
            id INTEGER PRIMARY KEY,
            page_url TEXT,
            source_id TEXT,
            title TEXT
        );
        CREATE TABLE media_files (
            id INTEGER PRIMARY KEY,
            media_id INTEGER NOT NULL REFERENCES media_items(id) ON DELETE CASCADE,
            path TEXT NOT NULL,
            hash TEXT,
            format TEXT NOT NULL
        );
        CREATE TABLE jobs (
            id INTEGER PRIMARY KEY,
            media_id INTEGER NOT NULL,
            kind TEXT NOT NULL,
            state TEXT NOT NULL,
            attempts INTEGER NOT NULL DEFAULT 0,
            error_kind TEXT,
            preset TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE TABLE playlists (
            id INTEGER PRIMARY KEY,
            page_url TEXT,
            title TEXT
        );
        CREATE TABLE playlist_items (
            playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
            media_id INTEGER NOT NULL REFERENCES media_items(id) ON DELETE CASCADE,
            position INTEGER NOT NULL,
            PRIMARY KEY (playlist_id, media_id)
        );
        CREATE TABLE audio_facts (
            id INTEGER PRIMARY KEY,
            media_file_id INTEGER NOT NULL REFERENCES media_files(id) ON DELETE CASCADE,
            facts_json TEXT NOT NULL
        );
        CREATE INDEX jobs_state ON jobs(state);
        CREATE INDEX media_items_source ON media_items(source_id);
        CREATE INDEX media_files_media ON media_files(media_id);
        """,
        """
        CREATE TABLE capture_keys (
            key_hash TEXT PRIMARY KEY,
            media_id INTEGER NOT NULL REFERENCES media_items(id) ON DELETE CASCADE
        );
        """,
        // The Library as the whole collection: where each item came from, and the light index kept for watched files.
        // Items with no link were opened from File Explorer before this step.
        """
        ALTER TABLE media_items ADD COLUMN origin TEXT NOT NULL DEFAULT 'downloaded';
        ALTER TABLE media_files ADD COLUMN size INTEGER;
        ALTER TABLE media_files ADD COLUMN modified TEXT;
        UPDATE media_items SET origin = 'opened' WHERE page_url IS NULL AND source_id IS NULL;
        CREATE INDEX media_files_path ON media_files(path COLLATE NOCASE);
        CREATE INDEX media_items_origin ON media_items(origin);
        """,
    ];

    public static int Latest => Steps.Length;

    public static void Apply(SqliteConnection connection)
    {
        for (int version = Db.Version(connection); version < Steps.Length; version++)
        {
            using var transaction = connection.BeginTransaction();
            using (var step = connection.CreateCommand())
            {
                step.Transaction = transaction;
                step.CommandText = Steps[version] + $"\nPRAGMA user_version = {version + 1};";
                step.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }
}
