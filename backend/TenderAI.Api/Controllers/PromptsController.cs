using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services;

namespace TenderAI.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public sealed class PromptsController(TenderAiDbContext db) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<PromptDto>>> Get(CancellationToken ct)
        {
            var saved = await db.PromptSettings.AsNoTracking().ToDictionaryAsync(p => p.Key, ct);
            return new[] { PromptDefinitions.Matching, PromptDefinitions.Summarization }
                .Select(key => ToDto(key, saved.GetValueOrDefault(key)))
                .ToArray();
        }

        [HttpPut("{key}")]
        public async Task<ActionResult<PromptDto>> Update(string key, UpdatePromptRequest request, CancellationToken ct)
        {
            var defaultInstructions = PromptDefinitions.DefaultFor(key);
            if (defaultInstructions is null) return NotFound();
            if (string.IsNullOrWhiteSpace(request.Instructions) || request.Instructions.Length > PromptSettingEntity.MaximumLength)
                return BadRequest(new ProblemDetails { Title = $"Instructions must contain 1 to {PromptSettingEntity.MaximumLength} characters." });
            if (request.Revision < 0)
                return BadRequest(new ProblemDetails { Title = "Revision must not be negative." });

            using var discoveryLock = key == PromptDefinitions.Matching ? await MatchingService.LockDiscoveryAsync(ct) : null;
            var setting = await db.PromptSettings.SingleOrDefaultAsync(p => p.Key == key, ct);
            if ((setting?.Revision ?? 0) != request.Revision)
                return Conflict(new ProblemDetails { Title = "This prompt was changed elsewhere. Reload the page before saving." });

            var instructions = request.Instructions.Trim();
            var savedInstructions = instructions == defaultInstructions ? null : instructions;
            var matchingChanged = key == PromptDefinitions.Matching
                && (setting?.Instructions ?? defaultInstructions) != instructions;
            if (setting is null)
            {
                setting = new PromptSettingEntity { Key = key, Instructions = savedInstructions, Revision = 1 };
                db.PromptSettings.Add(setting);
            }
            else
            {
                setting.Instructions = savedInstructions;
                setting.Revision++;
            }

            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.SaveChangesAsync(ct);
                if (matchingChanged) await db.TenderDiscoverySeen.ExecuteDeleteAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new ProblemDetails { Title = "This prompt was changed elsewhere. Reload the page before saving." });
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
            {
                return Conflict(new ProblemDetails { Title = "This prompt was changed elsewhere. Reload the page before saving." });
            }

            return ToDto(key, setting);
        }

        private static PromptDto ToDto(string key, PromptSettingEntity? setting)
        {
            var defaultInstructions = PromptDefinitions.DefaultFor(key)
                ?? throw new ArgumentOutOfRangeException(nameof(key));
            var requiredInstructions = PromptDefinitions.RequiredFor(key)
                ?? throw new ArgumentOutOfRangeException(nameof(key));
            return new PromptDto(key, setting?.Instructions ?? defaultInstructions, defaultInstructions,
                requiredInstructions, setting?.Revision ?? 0);
        }
    }

    public sealed record PromptDto(string Key, string Instructions, string DefaultInstructions, string RequiredInstructions, int Revision);
    public sealed record UpdatePromptRequest(string? Instructions, int Revision);
}
