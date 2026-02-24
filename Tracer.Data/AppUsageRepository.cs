using Dapper;
using Tracer.Shared.Models;

namespace Tracer.Data
{
    public class AppUsageRepository
    {
        private readonly Database _db;

        public AppUsageRepository(Database db)
        {
            _db = db;
        }

        public async Task LogIntervalAsync(string workerId, DateTime start, DateTime end, string appsJson)
        {
            using var conn = _db.GetConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO AppUsageLog (WorkerId, IntervalStart, IntervalEnd, Apps, Synced)
                VALUES (@WorkerId, @IntervalStart, @IntervalEnd, @Apps, 0)",
                new
                {
                    WorkerId = workerId,
                    IntervalStart = start.ToString("o"),
                    IntervalEnd = end.ToString("o"),
                    Apps = appsJson
                });
        }

        public async Task<IEnumerable<AppUsageLog>> GetUnsyncedAsync()
        {
            using var conn = _db.GetConnection();
            return await conn.QueryAsync<AppUsageLog>(
                "SELECT * FROM AppUsageLog WHERE Synced = 0");
        }

        public async Task MarkSyncedAsync(int id)
        {
            using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                "UPDATE AppUsageLog SET Synced = 1 WHERE Id = @Id",
                new { Id = id });
        }
    }
}