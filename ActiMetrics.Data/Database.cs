// Data/Database.cs
using System;
using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ActiMetrics.Data
{
    public class Database
    {
        private readonly string _connectionString;

        public Database()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tracer"
            );
            Directory.CreateDirectory(folder);

            string dbPath = Path.Combine(folder, "tracer.db");
            _connectionString = $"Data Source={dbPath}";

            Console.WriteLine($"[DB] Ruta: {dbPath}");
            Initialize();
        }

        private void Initialize()
        {
            using var conn = GetConnection();
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS StateLog (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    WorkerId  TEXT    NOT NULL,
                    WorkerUserName  TEXT    NOT NULL,
                    Category  TEXT    NOT NULL,
                    State     TEXT    NOT NULL,
                    Type      TEXT    NOT NULL, 
                    Timestamp TEXT    NOT NULL,
                    Synced    INTEGER NOT NULL DEFAULT 0
                );
            ");
            conn.Execute(@"
                    CREATE TABLE IF NOT EXISTS AppUsageLog (
                        Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                        WorkerId      TEXT    NOT NULL,
                        WorkerUserName      TEXT    NOT NULL,
                        Apps       TEXT    NOT NULL,
                        Seconds       REAL    NOT NULL DEFAULT 0,
                        IntervalStart TEXT    NOT NULL,
                        IntervalEnd   TEXT    NOT NULL,
                        Synced        INTEGER NOT NULL DEFAULT 0
                    );
                ");
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS Screenshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    WorkerId TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    Synced INTEGER NOT NULL DEFAULT 0);
            ");


        }

        public IDbConnection GetConnection() => new SqliteConnection(_connectionString);
    }
}