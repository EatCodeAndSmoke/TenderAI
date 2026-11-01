using System.Text.Json;
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
        public async Task<ActionResult<IEnumerable<TenderDto>>> GetAll(CancellationToken ct)
        {
            var tenders = await db.Tenders
                .AsNoTracking()
                .OrderBy(t => t.EndDate)
                .ThenBy(t => t.Id)
                .ToListAsync(ct);

            return tenders.Select(t => t.ToDto()).ToList();
        }

        // GET api/tenders/511680
        [HttpGet("{id:long}")]
        public async Task<ActionResult<TenderDto>> Get(long id, CancellationToken ct)
        {
            var tender = await db.Tenders.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
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

        // DELETE api/tenders
        // Removes every tender and product, along with matching notifications that refer to them.
        [HttpDelete]
        public async Task<IActionResult> Clear(CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.ExecuteDeleteAsync(ct);
            await db.TenderProducts.ExecuteDeleteAsync(ct);
            await db.Tenders.ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);

            return NoContent();
        }
    }
}
