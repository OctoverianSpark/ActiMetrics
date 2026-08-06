using Dapper;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Data
{
    public class TicketRepository
    {
        private readonly Database _db;

        public TicketRepository(Database db) => _db = db;

        // Devuelve el Id insertado para poder marcarlo Synced de inmediato si el envío sale bien
        // en el intento en línea, sin una consulta aparte.
        public async Task<int> InsertAsync(string category, string description, string usuario)
        {
            using var conn = _db.GetConnection();
            var id = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO Tickets (Category, Description, Usuario, CreatedAt, Synced)
                VALUES (@Category, @Description, @Usuario, @CreatedAt, 0);
                SELECT last_insert_rowid();",
                new
                {
                    Category = category,
                    Description = description,
                    Usuario = usuario,
                    CreatedAt = DateTime.Now.ToString("o")
                });
            return (int)id;
        }

        public async Task<IEnumerable<TicketRecord>> GetUnsyncedAsync()
        {
            using var conn = _db.GetConnection();
            return await conn.QueryAsync<TicketRecord>("SELECT * FROM Tickets WHERE Synced = 0");
        }

        public async Task MarkSyncedAsync(int id)
        {
            using var conn = _db.GetConnection();
            await conn.ExecuteAsync("UPDATE Tickets SET Synced = 1 WHERE Id = @Id", new { Id = id });
        }
    }
}
