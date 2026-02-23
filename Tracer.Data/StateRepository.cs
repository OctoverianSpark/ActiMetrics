using Dapper;
using Tracer.Shared.Models;

namespace Tracer.Data
{
    public class StateRepository
    {
        private readonly Database _db;

        public StateRepository(Database db)
        {
            _db = db;
        }

        // Inserta un nuevo cambio de estado
        public async Task LogStateAsync(string workerId, StateCategory category, WorkState state)
        {
            using var conn = _db.GetConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO StateLog (WorkerId, Category, State, Timestamp, Synced)
                VALUES (@WorkerId, @Category, @State, @Timestamp, 0)",
                new
                {
                    WorkerId = workerId,
                    Category = category.ToString(),
                    State = state.ToString(),
                    Timestamp = DateTime.Now.ToString("o")
                });
        }

        // Obtiene el último estado registrado
        public async Task<StateLog?> GetLastStateAsync(string workerId)
        {
            using var conn = _db.GetConnection();
            return await conn.QueryFirstOrDefaultAsync<StateLog>(@"
                SELECT * FROM StateLog
                WHERE WorkerId = @WorkerId
                ORDER BY Timestamp DESC
                LIMIT 1",
                new { WorkerId = workerId });
        }

        // Calcula el tiempo activo total de hoy
        public async Task<TimeSpan> GetActiveTodayAsync(string workerId)
        {
            using var conn = _db.GetConnection();
            var logs = await conn.QueryAsync<StateLog>(@"
                SELECT * FROM StateLog
                WHERE WorkerId = @WorkerId
                  AND date(Timestamp) = date('now')
                ORDER BY Timestamp ASC",
                new { WorkerId = workerId });

            return CalculateActiveTime(logs.ToList());
        }

        private TimeSpan CalculateActiveTime(List<StateLog> logs)
        {
            TimeSpan total = TimeSpan.Zero;

            for (int i = 0; i < logs.Count - 1; i++)
            {
                if (logs[i].Category == StateCategory.Active)
                {
                    var from = DateTime.Parse(logs[i].Timestamp.ToString());
                    var to = DateTime.Parse(logs[i + 1].Timestamp.ToString());
                    total += to - from;
                }
            }

            // Si el último estado es Active, contar hasta ahora
            if (logs.LastOrDefault()?.Category == StateCategory.Active)
            {
                var last = DateTime.Parse(logs.Last().Timestamp.ToString());
                total += DateTime.Now - last;
            }

            return total;
        }
    }
}