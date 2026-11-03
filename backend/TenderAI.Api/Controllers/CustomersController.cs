using System.Globalization;
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
    public class CustomersController(TenderAiDbContext db) : ControllerBase
    {
        // Demo authentication for the local prototype.
        private const string DemoPassword = "123";
        private static readonly SemaphoreSlim AccountBackfillLock = new(1, 1);

        private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

        // GET api/customers
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerRecordDto>>> GetAll(CancellationToken ct)
        {
            var customers = await db.Customers
                .AsNoTracking()
                .Include(c => c.RiskFeatures)
                .Include(c => c.Payments)
                .OrderBy(c => c.CustomerId)
                .AsSplitQuery()
                .ToListAsync(ct);

            return customers.Select(c => c.ToDto(Today)).ToList();
        }

        // GET api/customers/C-01
        [HttpGet("{customerId}")]
        public async Task<ActionResult<CustomerRecordDto>> Get(string customerId, CancellationToken ct)
        {
            var customer = await FindAsync(customerId, ct, tracking: false);
            return customer is null ? NotFound() : customer.ToDto(Today);
        }

        // POST api/customers
        [HttpPost]
        public async Task<ActionResult<CustomerRecordDto>> Add(CustomerRecordDto request, CancellationToken ct)
        {
            var customerId = NormalizeId(request.Customer!.CustomerId);

            if (await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct))
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: $"A customer with the ID {customerId} already exists.");
            }

            var customer = new CustomerEntity { CustomerId = customerId };
            customer.ApplyFrom(request);
            customer.Accounts = DemoAccounts.Create(customerId);

            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(Get), new { customerId }, customer.ToDto(Today));
        }

        // POST api/customers/generate
        [HttpPost("generate")]
        public async Task<ActionResult<IEnumerable<CustomerRecordDto>>> Generate(
            [FromServices] CustomerGenerationService generator,
            CancellationToken ct,
            [FromQuery] string? scenario = null,
            [FromServices] TenderImportService? importer = null,
            [FromServices] MatchingService? matching = null,
            [FromServices] TenderSummaryService? summaries = null)
        {
            if (scenario is not null && scenario is not ("offer" or "match" or "none"))
                return BadRequest("Scenario must be offer, match, or none.");
            try
            {
                if (scenario is not null && importer is not null
                    && !await db.Tenders.AnyAsync(t => t.EndDate >= DateTime.UtcNow.AddHours(5), ct))
                    await importer.LoadNewAsync(TenderImportService.MaxTendersPerLoad, ct);
                else if (scenario is not null && summaries is not null)
                    await summaries.SummarizeMissingTendersAsync(db, ct);
                try
                {
                    return Ok(await generator.GenerateAsync(scenario, ct, matching));
                }
                catch (NoAvailableTenderSummaryException) when (scenario is not null && importer is not null)
                {
                    var now = DateTime.UtcNow.AddHours(5);
                    if (!await db.Tenders.AnyAsync(t => t.EndDate >= now && t.Summary != null && t.Summary != ""
                            && t.EnglishTitle != null && t.EnglishTitle != "", ct)
                        && await db.Tenders.AnyAsync(t => t.EndDate >= now && t.SummaryError != null, ct))
                        throw;
                    await importer.LoadNewAsync(TenderImportService.MaxTendersPerLoad, ct);
                    return Ok(await generator.GenerateAsync(scenario, ct, matching));
                }
            }
            catch (NoAvailableTenderSummaryException ex)
            {
                var now = DateTime.UtcNow.AddHours(5);
                if (!await db.Tenders.AnyAsync(t => t.EndDate >= now && t.Summary != null && t.Summary != ""
                        && (scenario == null || (t.EnglishTitle != null && t.EnglishTitle != "")), ct)
                    && await db.Tenders.AnyAsync(t => t.EndDate >= now && t.SummaryError != null, ct))
                    return Problem(statusCode: StatusCodes.Status502BadGateway,
                        title: "Tender summaries are unavailable. Check the Amazon Bedrock configuration and try again.");
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }
            catch (CustomerProfileGenerationException ex)
            {
                return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Couldn't load tenders from UZEX.", detail: ex.Message);
            }
        }

        [HttpGet("{customerId}/accounts")]
        public async Task<IActionResult> Accounts(string customerId, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            await AccountBackfillLock.WaitAsync(ct);
            try
            {
                var customer = await db.Customers.Include(c => c.Accounts).FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
                if (customer is null) return NotFound();
                if (customer.Accounts.Count == 0)
                {
                    customer.Accounts = DemoAccounts.Create(customerId);
                    await db.SaveChangesAsync(ct);
                }
                return Ok(customer.Accounts.Select(a => new { a.Iban, a.Currency, a.Balance }));
            }
            finally { AccountBackfillLock.Release(); }
        }

        // PUT api/customers/C-01
        [HttpPut("{customerId}")]
        public async Task<ActionResult<CustomerRecordDto>> Update(string customerId, CustomerRecordDto request, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);

            if (NormalizeId(request.Customer!.CustomerId) != customerId)
            {
                ModelState.AddModelError("customer.customerId", "The customer ID can't be changed.");
                return ValidationProblem(ModelState);
            }

            var customer = await FindAsync(customerId, ct, tracking: true);
            if (customer is null) return NotFound();

            customer.ApplyFrom(request);
            await db.SaveChangesAsync(ct);

            return customer.ToDto(Today);
        }

        // DELETE api/customers/C-01
        // Removes one customer together with their risk features, payments and notifications.
        [HttpDelete("{customerId}")]
        public async Task<IActionResult> Delete(string customerId, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.Where(n => n.CustomerId == customerId).ExecuteDeleteAsync(ct);
            await db.CustomerAccounts.Where(a => a.CustomerId == customerId).ExecuteDeleteAsync(ct);
            await db.CustomerPayments.Where(p => p.CustomerId == customerId).ExecuteDeleteAsync(ct);
            await db.CustomerRiskFeatures.Where(r => r.CustomerId == customerId).ExecuteDeleteAsync(ct);
            var deleted = await db.Customers.Where(c => c.CustomerId == customerId).ExecuteDeleteAsync(ct);
            if (deleted == 0) return NotFound();
            await transaction.CommitAsync(ct);

            return NoContent();
        }

        // DELETE api/customers
        // Removes every customer together with their risk features, payments and notifications.
        [HttpDelete]
        public async Task<IActionResult> Clear(CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.ExecuteDeleteAsync(ct);
            await db.CustomerAccounts.ExecuteDeleteAsync(ct);
            await db.CustomerPayments.ExecuteDeleteAsync(ct);
            await db.CustomerRiskFeatures.ExecuteDeleteAsync(ct);
            await db.Customers.ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);

            return NoContent();
        }

        // POST api/customers/auth
        [HttpPost("auth")]
        public async Task<ActionResult<AuthResponse>> Auth(AuthRequest request, CancellationToken ct)
        {
            var customerId = NormalizeId(request.CustomerId);
            var customer = await db.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == customerId)
                .Select(c => new AuthResponse(c.CustomerId, c.Name))
                .FirstOrDefaultAsync(ct);

            if (customer is null || request.Password != DemoPassword)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "That customer ID or password is wrong.");
            }

            return customer;
        }

        [HttpGet("{customerId}/notifications")]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetNotifications(string customerId, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct)) return NotFound();
            return Array.Empty<NotificationDto>();
        }

        [HttpGet("{customerId}/matches")]
        public async Task<ActionResult<IEnumerable<NotificationDto>>> GetMatches(string customerId, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct)) return NotFound();

            var notifications = await db.Notifications
                .AsNoTracking()
                .Where(n => n.CustomerId == customerId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync(ct);
            var tenderIds = notifications
                .Select(notification => long.TryParse(notification.LotId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    ? (long?)id
                    : null)
                .Where(id => id.HasValue)
                .Select(id => id.GetValueOrDefault())
                .Distinct()
                .ToList();
            var tenderSummaries = (await db.Tenders
                .AsNoTracking()
                .Where(tender => tenderIds.Contains(tender.Id))
                .Select(tender => new { tender.Id, Title = tender.EnglishTitle ?? tender.Summary })
                .ToListAsync(ct))
                .ToDictionary(
                    tender => tender.Id.ToString(CultureInfo.InvariantCulture),
                    tender => tender.Title);

            return notifications
                .Select(notification => notification.ToDto(tenderSummaries.GetValueOrDefault(notification.LotId)))
                .ToList();
        }

        private Task<CustomerEntity?> FindAsync(string customerId, CancellationToken ct, bool tracking)
        {
            customerId = NormalizeId(customerId);
            var query = db.Customers
                .Include(c => c.RiskFeatures)
                .Include(c => c.Payments)
                .AsSplitQuery();
            if (!tracking) query = query.AsNoTracking();
            return query.FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
        }

        // Customer IDs are stored in upper case: c-01 and C-01 are the same customer
        private static string NormalizeId(string customerId) => customerId.Trim().ToUpperInvariant();
    }
}
