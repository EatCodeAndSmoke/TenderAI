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
        // columns explicitly so deployments keep their existing tender data and legacy translation rows.
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
                AddColumnIfMissing(db, connection, "Tenders", "Summary");
                AddColumnIfMissing(db, connection, "Tenders", "SummaryVersion");
                AddColumnIfMissing(db, connection, "Tenders", "SummaryError");
                AddColumnIfMissing(db, connection, "Tenders", "DetailedName");
                AddColumnIfMissing(db, connection, "Tenders", "AdditionalDescription");
                AddColumnIfMissing(db, connection, "Tenders", "HasUnparsedTechnicalDocuments");
                AddColumnIfMissing(db, connection, "Tenders", "BuyerRequirements");
                AddColumnIfMissing(db, connection, "TenderProducts", "TechnicalSpecifications");
            }
            finally
            {
                if (!wasOpen)
                {
                    db.Database.CloseConnection();
                }
            }
        }

        private static void AddColumnIfMissing(
            TenderAiDbContext db,
            System.Data.Common.DbConnection connection,
            string table,
            string column)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info(\"{table}\")";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    columns.Add(reader.GetString(1));
                }
            }

            if (!columns.Contains(column))
            {
                var sql = (table, column) switch
                {
                    ("Tenders", "Summary") => "ALTER TABLE \"Tenders\" ADD COLUMN \"Summary\" TEXT NULL",
                    ("Tenders", "SummaryVersion") => "ALTER TABLE \"Tenders\" ADD COLUMN \"SummaryVersion\" INTEGER NULL",
                    ("Tenders", "SummaryError") => "ALTER TABLE \"Tenders\" ADD COLUMN \"SummaryError\" TEXT NULL",
                    ("Tenders", "DetailedName") => "ALTER TABLE \"Tenders\" ADD COLUMN \"DetailedName\" TEXT NULL",
                    ("Tenders", "AdditionalDescription") => "ALTER TABLE \"Tenders\" ADD COLUMN \"AdditionalDescription\" TEXT NULL",
                    ("Tenders", "HasUnparsedTechnicalDocuments") => "ALTER TABLE \"Tenders\" ADD COLUMN \"HasUnparsedTechnicalDocuments\" INTEGER NULL",
                    ("Tenders", "BuyerRequirements") => "ALTER TABLE \"Tenders\" ADD COLUMN \"BuyerRequirements\" TEXT NOT NULL DEFAULT '[]'",
                    ("TenderProducts", "TechnicalSpecifications") => "ALTER TABLE \"TenderProducts\" ADD COLUMN \"TechnicalSpecifications\" TEXT NULL",
                    _ => throw new ArgumentOutOfRangeException(nameof(column), $"Unsupported schema column {table}.{column}.")
                };
                db.Database.ExecuteSqlRaw(sql);
            }
        }

        [GeneratedRegex(@"^CREATE\s+(?:UNIQUE\s+)?(?:TABLE|INDEX)\s+""(?<name>[^""]+)""")]
        private static partial Regex CreatedObject();
    }
}
