using System.Globalization;
using Microsoft.Data.Sqlite;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Logging;
using Stx.Core.Queue;

namespace Stx.Data;

public sealed class JobRepo(SqliteConnection connection) : IJobRepo
{
    const string Fields = "id, media_id, kind, state, attempts, error_kind, preset, created_at";

    public Job Add(Job job)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO jobs (media_id, kind, state, attempts, error_kind, preset, created_at)
                VALUES ($media, $kind, $state, $attempts, $error, $preset, $created)
                RETURNING id;
                """;
            Bind(command, job);
            return job with { Id = Convert.ToInt64(command.ExecuteScalar()) };
        }
    }

    public void Update(Job job)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE jobs SET media_id = $media, kind = $kind, state = $state, attempts = $attempts,
                    error_kind = $error, preset = $preset, created_at = $created
                WHERE id = $id;
                """;
            Bind(command, job);
            command.Parameters.AddWithValue("$id", job.Id);
            command.ExecuteNonQuery();
        }
    }

    public Job? Get(long id)
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Fields} FROM jobs WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
    }

    public IReadOnlyList<Job> All()
    {
        lock (connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Fields} FROM jobs ORDER BY id;";
            using var reader = command.ExecuteReader();
            var jobs = new List<Job>();
            while (reader.Read())
                if (Read(reader) is { } job) jobs.Add(job);
            return jobs;
        }
    }

    static void Bind(SqliteCommand command, Job job)
    {
        command.Parameters.AddWithValue("$media", job.MediaId);
        command.Parameters.AddWithValue("$kind", job.Kind.ToString());
        command.Parameters.AddWithValue("$state", job.State.ToString());
        command.Parameters.AddWithValue("$attempts", job.Attempts);
        command.Parameters.AddWithValue("$error", (object?)job.Error?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$preset", job.Preset);
        command.Parameters.AddWithValue("$created", job.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
    }

    // A row this version can't read (written by a newer STX, or damaged) is left out and logged. Review 2026-11-07:
    // one such row made every read of the queue throw, and with it the queue's start, every pump and the plate.
    // An error kind it doesn't know reads as a tool failure: the job itself is still sound.
    static Job? Read(SqliteDataReader reader)
    {
        long id = reader.GetInt64(0);
        if (!Enum.TryParse<JobKind>(reader.GetString(2), out var kind) || !Enum.TryParse<JobState>(reader.GetString(3), out var state)
            || !DateTimeOffset.TryParse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
        {
            ErrorLog.Write("queue", $"job {id} can't be read by this version (kind {reader.GetString(2)}, state {reader.GetString(3)}); it's left out");
            return null;
        }
        ErrorKind? error = reader.IsDBNull(5) ? null : Enum.TryParse<ErrorKind>(reader.GetString(5), out var known) ? known : ErrorKind.ToolFailure;
        return new(id, reader.GetInt64(1), kind, state, reader.GetInt32(4), error, reader.GetString(6), created);
    }
}
