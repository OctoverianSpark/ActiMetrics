using Dapper;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Data
{
    public class AppUsageRepository
    {
        private readonly Database _db;

        public AppUsageRepository(Database db) => _db = db;

        public async Task LogIntervalAsync(
            string workerId, string workerUserName,
            DateTime start, DateTime end,
            string appsJson,
            int activeSeconds, int idleSeconds,
            int mouseClicks, int keystrokes)
        {
            using var conn = _db.GetConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO AppUsageLog
                    (WorkerId, WorkerUserName, IntervalStart, IntervalEnd, Apps,
                     ActiveSeconds, IdleSeconds, MouseClicks, Keystrokes, Synced)
                VALUES
                    (@WorkerId, @WorkerUserName, @IntervalStart, @IntervalEnd, @Apps,
                     @ActiveSeconds, @IdleSeconds, @MouseClicks, @Keystrokes, 0)",
                new
                {
                    WorkerId       = workerId,
                    WorkerUserName = workerUserName,
                    IntervalStart  = start.ToString("o"),
                    IntervalEnd    = end.ToString("o"),
                    Apps           = appsJson,
                    ActiveSeconds  = activeSeconds,
                    IdleSeconds    = idleSeconds,
                    MouseClicks    = mouseClicks,
                    Keystrokes     = keystrokes
                });
        }

        public async Task<IEnumerable<AppUsageLog>> GetUnsyncedAsync()
        {
            using var conn = _db.GetConnection();
            return await conn.QueryAsync<AppUsageLog>("SELECT * FROM AppUsageLog WHERE Synced = 0");
        }

        public async Task MarkSyncedAsync(int id)
        {
            using var conn = _db.GetConnection();
            await conn.ExecuteAsync("UPDATE AppUsageLog SET Synced = 1 WHERE Id = @Id", new { Id = id });
        }
    }
}
