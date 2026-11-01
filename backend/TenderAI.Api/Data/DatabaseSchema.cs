using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace TenderAI.Api.Data
{
    public static partial class DatabaseSchema
    {
        // EnsureCreated only creates a database that doesn't exist yet. When the model gets new tables
        // (like the tender tables), this adds them to an existing database and keeps its data.
        // Changes to columns of existing tables still need the .db file to be deleted.
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

        [GeneratedRegex(@"^CREATE\s+(?:UNIQUE\s+)?(?:TABLE|INDEX)\s+""(?<name>[^""]+)""")]
        private static partial Regex CreatedObject();
    }
}
