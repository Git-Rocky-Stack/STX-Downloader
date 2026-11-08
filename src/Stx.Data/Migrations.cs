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
        // Library rows out of memory (plan 2026-11-05): the Library is counted, paged and searched here, so the app
        // holds only the rows on screen.
        // - path_key and title_key are the path and title with every letter's case folded (stx_fold, CaseKey.Of in
        //   .NET). SQLite's NOCASE folds A to Z only, so "CAFÉ" and "café" were two paths to it and one file to
        //   Windows. A search word, folded the same way, is found with instr: exactly OrdinalIgnoreCase's rule, the
        //   in-memory search's, at any length.
        // - listed marks the one row shown for a path: the first saved (two links can lead to one file). Testing it
        //   per row, with a lookup for an earlier row, cost 120 ms over 50,000 rows once the database outgrew
        //   SQLite's page cache; a flag costs nothing to read. media_files_listed walks the listed rows in the
        //   Library's order: the newest item first, then its files as saved.
        // - The triggers keep all three through every write, foreign-key cascades included, so no writer can leave
        //   them out. They call stx_fold, which Db.AddFunctions gives every connection STX opens.
        // A Library made by hand in a test may lack the NOCASE index, hence IF EXISTS.
        """
        ALTER TABLE media_files ADD COLUMN path_key TEXT;
        ALTER TABLE media_files ADD COLUMN listed INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE media_items ADD COLUMN title_key TEXT;
        UPDATE media_files SET path_key = stx_fold(path);
        UPDATE media_items SET title_key = stx_fold(title);
        CREATE INDEX media_files_path_key ON media_files(path_key, id);
        DROP INDEX IF EXISTS media_files_path;
        UPDATE media_files SET listed = (id = (SELECT min(k.id) FROM media_files k WHERE k.path_key = media_files.path_key));
        CREATE INDEX media_files_listed ON media_files(media_id DESC, id) WHERE listed = 1;
        CREATE TRIGGER media_files_added AFTER INSERT ON media_files BEGIN
            UPDATE media_files SET path_key = stx_fold(new.path) WHERE id = new.id;
            UPDATE media_files SET listed = (id = (SELECT min(k.id) FROM media_files k WHERE k.path_key = media_files.path_key))
                WHERE path_key = stx_fold(new.path);
        END;
        CREATE TRIGGER media_files_moved AFTER UPDATE OF path ON media_files BEGIN
            UPDATE media_files SET path_key = stx_fold(new.path) WHERE id = new.id;
            UPDATE media_files SET listed = (id = (SELECT min(k.id) FROM media_files k WHERE k.path_key = media_files.path_key))
                WHERE path_key IN (old.path_key, stx_fold(new.path));
        END;
        CREATE TRIGGER media_files_gone AFTER DELETE ON media_files BEGIN
            UPDATE media_files SET listed = (id = (SELECT min(k.id) FROM media_files k WHERE k.path_key = media_files.path_key))
                WHERE path_key = old.path_key;
        END;
        CREATE TRIGGER media_items_added AFTER INSERT ON media_items BEGIN
            UPDATE media_items SET title_key = stx_fold(new.title) WHERE id = new.id;
        END;
        CREATE TRIGGER media_items_retitled AFTER UPDATE OF title ON media_items BEGIN
            UPDATE media_items SET title_key = stx_fold(new.title) WHERE id = new.id;
        END;
        """,
    ];

    public static int Latest => Steps.Length;

    public static void Apply(SqliteConnection connection) => Apply(connection, Steps.Length);

    /// <summary>Brings the schema up to step <paramref name="through"/> and no further: how a test makes the Library an older STX left.</summary>
    public static void Apply(SqliteConnection connection, int through)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(through, Steps.Length);
        Db.AddFunctions(connection);
        for (int version = Db.Version(connection); version < through; version++)
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
