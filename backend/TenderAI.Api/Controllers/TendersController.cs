using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;
using TenderAI.Api.Services;

namespace TenderAI.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TendersController(TenderAiDbContext db) : ControllerBase
    {
        private const int DefaultLoadCount = TenderImportService.MaxTendersPerLoad;

        // GET api/tenders
        // Nearest deadline first
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TenderDto>>> GetAll(CancellationToken ct, [FromQuery] bool includeExpired = false)
        {
            var tenders = await db.Tenders
                .AsNoTracking()
                .Where(t => !t.IsDeleted && (includeExpired || t.EndDate >= DateTime.UtcNow.AddHours(5)))
                .OrderBy(t => t.EndDate)
                .ThenBy(t => t.Id)
                .ToListAsync(ct);

            return tenders.Select(t => t.ToDto()).ToList();
        }

        // GET api/tenders/511680
        [HttpGet("{id:long}")]
        public async Task<ActionResult<TenderDto>> Get(long id, CancellationToken ct)
        {
            var tender = await db.Tenders.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
            return tender is null ? NotFound() : tender.ToDto();
        }

        // POST api/tenders/load?count=5
        // Loads lots from UZEX that aren't in the database yet and returns them. An empty list means
        // every open lot on UZEX is already loaded.
        [HttpPost("load")]
        public async Task<ActionResult<IEnumerable<TenderDto>>> Load(
            [FromServices] TenderImportService importer,
            [FromQuery] int count = DefaultLoadCount,
            CancellationToken ct = default)
        {
            count = Math.Clamp(count, 1, TenderImportService.MaxTendersPerLoad);

            List<TenderEntity> loaded;
            try
            {
                loaded = await importer.LoadNewAsync(count, ct);
            }

            catch (Exception ex) when (ex is HttpRequestException or JsonException
                || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "Couldn't get tenders from UZEX. Try again in a minute.",
                    detail: ex.Message);
            }

            return loaded
                .OrderBy(t => t.EndDate)
                .ThenBy(t => t.Id)
                .Select(t => t.ToDto())
                .ToList();
        }

        [HttpPost("load/stream")]
        public async Task LoadStreaming(
            [FromServices] TenderImportService importer,
            [FromServices] ILogger<TendersController> logger,
            [FromQuery] int count = DefaultLoadCount,
            CancellationToken ct = default)
        {
            if (count < 1 || count > TenderImportService.MaxTendersPerLoad)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new ProblemDetails { Title = $"Count must be between 1 and {TenderImportService.MaxTendersPerLoad}." }, ct);
                return;
            }
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache, no-store";
            Response.Headers.Append("X-Accel-Buffering", "no");
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            try
            {
                await foreach (var tender in importer.LoadNewStreamingAsync(count, ct))
                {
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "tender", tender = tender.ToDto() }, options)}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
                await Response.WriteAsync("data: {\"type\":\"completed\"}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Streaming UZEX tenders failed.");
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", message = "Could not load tenders from UZEX. Already loaded tenders were saved." }, options)}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct)
        {
            var tender = await db.Tenders.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
            if (tender is null) return NotFound();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.Where(n => n.LotId == id.ToString()).ExecuteDeleteAsync(ct);
            await db.TenderFeedback.Where(f => f.TenderId == id).ExecuteDeleteAsync(ct);
            await db.TenderApplications.Where(a => a.TenderId == id).ExecuteDeleteAsync(ct);
            tender.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return NoContent();
        }

        // DELETE api/tenders
        // Removes every tender and product, along with matching notifications that refer to them.
        [HttpDelete]
        public async Task<IActionResult> Clear(CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.ExecuteDeleteAsync(ct);
            await db.TenderFeedback.ExecuteDeleteAsync(ct);
            await db.TenderApplications.ExecuteDeleteAsync(ct);
            await db.Tenders.Where(t => !t.IsDeleted).ExecuteUpdateAsync(setters => setters.SetProperty(t => t.IsDeleted, true), ct);
            await transaction.CommitAsync(ct);

            return NoContent();
        }
    }
}
