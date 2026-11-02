using System.Globalization;
using Microsoft.Data.Sqlite;
using Stx.Core.Errors;
using Stx.Core.Jobs;
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
            while (reader.Read()) jobs.Add(Read(reader));
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

    static Job Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt64(1),
        Enum.Parse<JobKind>(reader.GetString(2)),
        Enum.Parse<JobState>(reader.GetString(3)),
        reader.GetInt32(4),
        reader.IsDBNull(5) ? null : Enum.Parse<ErrorKind>(reader.GetString(5)),
        reader.GetString(6),
        DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}
