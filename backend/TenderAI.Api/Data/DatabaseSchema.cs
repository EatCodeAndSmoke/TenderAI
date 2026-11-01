using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace TenderAI.Api.Data
{
    public static partial class DatabaseSchema
    {
        // EnsureCreated only creates a database that doesn't exist yet. When the model gets new tables
        // (like the tender tables), this adds them to an existing database and keeps its data.
        public static void AddMissingTables(TenderAiDbContext db)
        {
            var existing = db.Database
                .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type IN ('table', 'index')")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var statements = db.Database.GenerateCreateScript()
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // The script creates each table before its indexes, so the order is kept
            foreach (var sql in statements)
            {
                var match = CreatedObject().Match(sql);
                if (match.Success && !existing.Contains(match.Groups["name"].Value))
                {
                    db.Database.ExecuteSqlRaw(sql);
                }
            }
        }

        // SQLite's EnsureCreated doesn't add columns to an existing database. Add new nullable
        // columns explicitly so deployments keep their existing tender data.
        public static void AddMissingColumns(TenderAiDbContext db)
        {
            var connection = db.Database.GetDbConnection();
            var wasOpen = connection.State == ConnectionState.Open;
            if (!wasOpen)
            {
                db.Database.OpenConnection();
            }

            try
            {
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info('Tenders')";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        columns.Add(reader.GetString(1));
                    }
                }

                if (!columns.Contains("TranslationError"))
                {
                    db.Database.ExecuteSqlRaw(
                        "ALTER TABLE \"Tenders\" ADD COLUMN \"TranslationError\" TEXT NULL");
                }
            }
            finally
            {
                if (!wasOpen)
                {
                    db.Database.CloseConnection();
                }
            }
        }

        [GeneratedRegex(@"^CREATE\s+(?:UNIQUE\s+)?(?:TABLE|INDEX)\s+""(?<name>[^""]+)""")]
        private static partial Regex CreatedObject();
    }
}
