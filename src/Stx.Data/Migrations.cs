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
        // Review 2026-11-07: the first scan and the watcher could both add the same file, so a watched or opened file
        // could have two rows. The later row of each pair goes; a shrink copy or a job that hung off it moves to the
        // row that stays first, so nothing else is lost. Downloads are never removed. Files on disk aren't touched.
        """
        CREATE TEMP TABLE stx_duplicates AS
            SELECT f.id AS file_id, f.media_id AS media_id,
                   (SELECT k.media_id FROM media_files k WHERE k.path = f.path COLLATE NOCASE AND k.id < f.id ORDER BY k.id LIMIT 1) AS keep_media
            FROM media_files f JOIN media_items i ON i.id = f.media_id
            WHERE i.origin IN ('watched', 'opened')
              AND EXISTS (SELECT 1 FROM media_files k WHERE k.path = f.path COLLATE NOCASE AND k.id < f.id);
        UPDATE media_files SET media_id = (SELECT d.keep_media FROM stx_duplicates d WHERE d.media_id = media_files.media_id)
            WHERE media_id IN (SELECT media_id FROM stx_duplicates) AND id NOT IN (SELECT file_id FROM stx_duplicates);
        UPDATE jobs SET media_id = (SELECT d.keep_media FROM stx_duplicates d WHERE d.media_id = jobs.media_id)
            WHERE media_id IN (SELECT media_id FROM stx_duplicates);
        DELETE FROM media_files WHERE id IN (SELECT file_id FROM stx_duplicates);
        DELETE FROM media_items WHERE id IN (SELECT media_id FROM stx_duplicates)
            AND NOT EXISTS (SELECT 1 FROM media_files f WHERE f.media_id = media_items.id);
        DROP TABLE stx_duplicates;
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
