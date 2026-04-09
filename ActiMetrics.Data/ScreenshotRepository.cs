using Dapper;
using System;
using System.Collections.Generic;
using System.Text;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Data
{
    public class ScreenshotRepository
    {
        private readonly Database _db;

        public ScreenshotRepository(Database db)
        {
            _db = db;
        }


        public async Task LogIntervalAsync(string workerId, string filePath)
        {
            using var conn = _db.GetConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO  Screenshots (WorkerId, FilePath,  Synced)
                VALUES (@WorkerId,@FilePath, 0)",
                new
                {
                    WorkerId = workerId,
                    FilePath = filePath
                });
        }

        public async Task<IEnumerable<Screenshot>> GetUnsyncedAsync()
        {
            using var conn = _db.GetConnection();
            return await conn.QueryAsync<Screenshot>(
                "SELECT * FROM Screenshots WHERE Synced = 0");
        }

        public async Task MarkSyncedAsync(int id)
        {
            using var conn = _db.GetConnection();
            await conn.ExecuteAsync(
                "UPDATE Screenshots SET Synced = 1 WHERE Id = @Id",
                new { Id = id });
        }

    }
}
