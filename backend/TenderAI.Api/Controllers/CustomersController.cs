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
            CancellationToken ct)
        {
            return Ok(await generator.GenerateAsync(ct));
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

            using var discoveryLock = await MatchingService.LockDiscoveryAsync(ct);
            var customer = await FindAsync(customerId, ct, tracking: true);
            if (customer is null) return NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            customer.ApplyFrom(request);
            await db.SaveChangesAsync(ct);
            await db.TenderDiscoverySeen.Where(s => s.CustomerId == customerId).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);

            return customer.ToDto(Today);
        }

        // DELETE api/customers/C-01
        // Removes one customer together with their risk features, payments and notifications.
        [HttpDelete("{customerId}")]
        public async Task<IActionResult> Delete(string customerId, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);

            using var discoveryLock = await MatchingService.LockDiscoveryAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.Where(n => n.CustomerId == customerId).ExecuteDeleteAsync(ct);
            await db.TenderFeedback.Where(f => f.CustomerId == customerId).ExecuteDeleteAsync(ct);
            await db.TenderApplications.Where(a => a.CustomerId == customerId).ExecuteDeleteAsync(ct);
            await db.TenderDiscoverySeen.Where(s => s.CustomerId == customerId).ExecuteDeleteAsync(ct);
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
            using var discoveryLock = await MatchingService.LockDiscoveryAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.ExecuteDeleteAsync(ct);
            await db.TenderFeedback.ExecuteDeleteAsync(ct);
            await db.TenderApplications.ExecuteDeleteAsync(ct);
            await db.TenderDiscoverySeen.ExecuteDeleteAsync(ct);
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
        public Task<ActionResult<IEnumerable<NotificationDto>>> GetMatches(string customerId, CancellationToken ct) =>
            GetMatchesCore(customerId, includeExpired: false, ct);

        [HttpGet("{customerId}/discoveries")]
        public async Task<ActionResult<IEnumerable<SavedTenderDiscovery>>> GetDiscoveries(string customerId, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            var matches = await GetMatchesCore(customerId, includeExpired: true, ct);
            if (matches.Result is not null) return matches.Result;
            var saved = matches.Value!.ToList();
            var ids = saved.Select(match => long.Parse(match.LotId, CultureInfo.InvariantCulture)).ToList();
            var tenders = await db.Tenders.AsNoTracking().Where(t => ids.Contains(t.Id) && !t.IsDeleted)
                .ToDictionaryAsync(t => t.Id, ct);
            var now = DateTime.UtcNow.AddHours(5);
            return saved.Where(match => tenders.ContainsKey(long.Parse(match.LotId, CultureInfo.InvariantCulture)))
                .Select(match =>
                {
                    var tender = tenders[long.Parse(match.LotId, CultureInfo.InvariantCulture)];
                    return new SavedTenderDiscovery(tender.ToDto(), match, tender.EndDate >= now);
                }).ToList();
        }

        private async Task<ActionResult<IEnumerable<NotificationDto>>> GetMatchesCore(string customerId, bool includeExpired, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            var customer = await db.Customers.AsNoTracking().Include(c => c.RiskFeatures)
                .SingleOrDefaultAsync(c => c.CustomerId == customerId, ct);
            if (customer is null) return NotFound();
            var now = DateTime.UtcNow.AddHours(5);

            var notifications = await db.Notifications
                .AsNoTracking()
                .Where(n => n.CustomerId == customerId)
                .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
                .ToListAsync(ct);
            var feedback = (await db.TenderFeedback.AsNoTracking()
                .Where(f => f.CustomerId == customerId).ToListAsync(ct))
                .ToDictionary(f => f.TenderId.ToString(CultureInfo.InvariantCulture), f => f.Value);
            var applications = (await db.TenderApplications.AsNoTracking()
                .Where(a => a.CustomerId == customerId).ToListAsync(ct))
                .ToDictionary(a => a.TenderId.ToString(CultureInfo.InvariantCulture),
                    a => DateTime.SpecifyKind(a.AppliedAt, DateTimeKind.Utc));
            var tenderIds = notifications
                .Select(notification => long.TryParse(notification.LotId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    ? (long?)id
                    : null)
                .Where(id => id.HasValue)
                .Select(id => id.GetValueOrDefault())
                .Distinct()
                .ToList();
            var tenders = (await db.Tenders
                .AsNoTracking().Include(tender => tender.Products)
                .Where(tender => tenderIds.Contains(tender.Id) && !tender.IsDeleted)
                .AsSplitQuery()
                .ToListAsync(ct))
                .ToDictionary(
                    tender => tender.Id.ToString(CultureInfo.InvariantCulture),
                    tender => tender);

            return notifications
                .Where(notification => tenders.ContainsKey(notification.LotId))
                .GroupBy(notification => TenderIdentity.For(tenders[notification.LotId]) ?? $"lot:{notification.LotId}", StringComparer.Ordinal)
                .Where(group => !group.Any(notification => feedback.GetValueOrDefault(notification.LotId) == "dislike"))
                .Where(group => includeExpired || group.Any(notification => tenders[notification.LotId].EndDate >= now))
                .Select(group => group.OrderByDescending(notification => tenders[notification.LotId].EndDate >= now)
                    .ThenByDescending(notification => applications.ContainsKey(notification.LotId))
                    .ThenByDescending(notification => feedback.GetValueOrDefault(notification.LotId) == "like")
                    .ThenByDescending(notification => notification.CreatedAt)
                    .ThenByDescending(notification => notification.Id).First())
                .OrderByDescending(notification => notification.CreatedAt).ThenByDescending(notification => notification.Id)
                .Select(notification =>
                {
                    var tender = tenders[notification.LotId];
                    var (offer, reason) = MatchingService.GetCurrentLoanDecision(customer, tender);
                    return notification.ToDto(tender.EnglishTitle ?? tender.Summary) with
                    {
                        LoanDecisionReason = reason,
                        Offer = offer,
                        Feedback = feedback.GetValueOrDefault(notification.LotId),
                        AppliedAt = applications.TryGetValue(notification.LotId, out var appliedAt) ? appliedAt : null
                    };
                })
                .ToList();
        }

        [HttpPut("{customerId}/tenders/{tenderId:long}/feedback")]
        public async Task<IActionResult> SetFeedback(string customerId, long tenderId, [FromBody] TenderFeedbackRequest request, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            if (request.Value is not ("like" or "dislike")) return BadRequest("Feedback must be like or dislike.");
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct)
                || !await db.Tenders.AnyAsync(t => t.Id == tenderId && !t.IsDeleted, ct)) return NotFound();
            var feedback = await db.TenderFeedback.FindAsync([customerId, tenderId], ct);
            if (feedback is null) db.TenderFeedback.Add(new TenderFeedbackEntity { CustomerId = customerId, TenderId = tenderId, Value = request.Value });
            else feedback.Value = request.Value;
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpPut("{customerId}/tenders/{tenderId:long}/application")]
        public async Task<ActionResult<TenderApplicationDto>> Apply(string customerId, long tenderId, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            var lotId = tenderId.ToString(CultureInfo.InvariantCulture);
            var now = DateTime.UtcNow;
            var localNow = now.AddHours(5);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TenderApplications" ("CustomerId", "TenderId", "AppliedAt")
                SELECT {customerId}, {tenderId}, {now}
                WHERE EXISTS (SELECT 1 FROM "Customers" WHERE "CustomerId" = {customerId})
                  AND EXISTS (SELECT 1 FROM "Tenders"
                              WHERE "Id" = {tenderId} AND "IsDeleted" = 0 AND "EndDate" >= {localNow})
                  AND EXISTS (SELECT 1 FROM "Notifications"
                              WHERE "CustomerId" = {customerId} AND "LotId" = {lotId})
                  AND NOT EXISTS (SELECT 1 FROM "TenderFeedback"
                                  WHERE "CustomerId" = {customerId} AND "TenderId" = {tenderId} AND "Value" = 'dislike')
                ON CONFLICT ("CustomerId", "TenderId") DO NOTHING
                """, ct);

            var application = await db.TenderApplications.AsNoTracking()
                .SingleOrDefaultAsync(a => a.CustomerId == customerId && a.TenderId == tenderId, ct);
            if (application is not null) return ToApplicationDto(application);
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct)
                || !await db.Tenders.AnyAsync(t => t.Id == tenderId && !t.IsDeleted, ct)) return NotFound();
            return Conflict(new ProblemDetails { Title = "Only an open, matching tender can be marked for preparation." });
        }

        private static TenderApplicationDto ToApplicationDto(TenderApplicationEntity application) =>
            new("applied", DateTime.SpecifyKind(application.AppliedAt, DateTimeKind.Utc));

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

    public record TenderFeedbackRequest(string Value);
    public record TenderApplicationDto(string Status, DateTime AppliedAt);
    public record SavedTenderDiscovery(TenderDto Tender, NotificationDto Match, bool IsOpen);
}
