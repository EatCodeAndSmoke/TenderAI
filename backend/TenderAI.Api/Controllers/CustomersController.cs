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
        private static readonly TimeSpan SimulationDelay = TimeSpan.FromSeconds(12);
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
        public async Task<ActionResult<IEnumerable<ApplicationUpdateDto>>> GetNotifications(string customerId, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct)) return NotFound();
            var cutoff = DateTime.UtcNow - SimulationDelay;
            var updates = await (from application in db.TenderApplications.AsNoTracking()
                                 join tender in db.Tenders.AsNoTracking() on application.TenderId equals tender.Id
                                 where application.CustomerId == customerId && application.SimulatedOutcome != null
                                     && application.AppliedAt <= cutoff && !tender.IsDeleted
                                 orderby application.AppliedAt descending, application.TenderId descending
                                 select new { application, tender.DisplayNo, tender.EnglishTitle, tender.Summary, tender.Name }).ToListAsync(ct);
            var titles = (await db.Notifications.AsNoTracking().Where(n => n.CustomerId == customerId)
                .OrderByDescending(n => n.CreatedAt).Select(n => new { n.LotId, n.Title }).ToListAsync(ct))
                .GroupBy(n => n.LotId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Title, StringComparer.Ordinal);
            return updates.Select(update => new ApplicationUpdateDto(
                update.application.TenderId,
                update.DisplayNo,
                update.EnglishTitle ?? update.Summary
                    ?? titles.GetValueOrDefault(update.application.TenderId.ToString(CultureInfo.InvariantCulture)) ?? update.Name,
                update.application.SimulatedOutcome == "needs_details" ? "sent" : update.application.SimulatedOutcome!,
                DateTime.SpecifyKind(update.application.AppliedAt + SimulationDelay, DateTimeKind.Utc),
                update.application.NotificationReadAt.HasValue)).ToList();
        }

        [HttpPut("{customerId}/notifications/read")]
        public async Task<IActionResult> MarkNotificationsRead(string customerId, [FromBody] MarkApplicationUpdatesReadRequest request, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            if (request.TenderIds is null || request.TenderIds.Count == 0 || request.TenderIds.Count > 500)
                return BadRequest("Provide between 1 and 500 tender IDs.");
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct)) return NotFound();
            var now = DateTime.UtcNow;
            var cutoff = now - SimulationDelay;
            await db.TenderApplications.Where(a => a.CustomerId == customerId && a.SimulatedOutcome != null
                    && request.TenderIds.Contains(a.TenderId) && a.AppliedAt <= cutoff && a.NotificationReadAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.NotificationReadAt, now), ct);
            return NoContent();
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
            var tenders = await db.Tenders.AsNoTracking().Include(t => t.Products).Where(t => ids.Contains(t.Id) && !t.IsDeleted)
                .ToDictionaryAsync(t => t.Id, ct);
            var now = DateTime.UtcNow.AddHours(5);
            return saved.Where(match => tenders.ContainsKey(long.Parse(match.LotId, CultureInfo.InvariantCulture)))
                .Select(match =>
                {
                    var tender = tenders[long.Parse(match.LotId, CultureInfo.InvariantCulture)];
                    return new SavedTenderDiscovery(tender.ToDto() with
                    {
                        EnglishTitle = string.IsNullOrWhiteSpace(tender.EnglishTitle) ? match.Title : tender.EnglishTitle,
                        Summary = string.IsNullOrWhiteSpace(tender.Summary) ? match.Body : tender.Summary
                    }, match, tender.EndDate >= now, TenderIdentity.GroupKey(tender));
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
            var tenders = (await db.Tenders
                .AsNoTracking().Include(tender => tender.Products)
                .Where(tender => !tender.IsDeleted)
                .AsSplitQuery()
                .ToListAsync(ct))
                .ToDictionary(
                    tender => tender.Id.ToString(CultureInfo.InvariantCulture),
                    tender => tender);
            var tendersByGroup = tenders.Values
                .GroupBy(TenderIdentity.GroupKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var matchesByGroup = notifications
                .Where(notification => tenders.ContainsKey(notification.LotId))
                .GroupBy(notification => TenderIdentity.GroupKey(tenders[notification.LotId]), StringComparer.Ordinal);
            var results = new List<NotificationDto>();
            foreach (var group in matchesByGroup)
            {
                var relatedTenders = tendersByGroup[group.Key];
                if (relatedTenders.Any(t => feedback.GetValueOrDefault(t.Id.ToString(CultureInfo.InvariantCulture)) == "dislike"))
                    continue;
                var preferred = group.OrderByDescending(notification => notification.MatchScore >= MatchingService.MinimumMatchScore)
                    .ThenByDescending(notification => tenders[notification.LotId].EndDate >= now)
                    .ThenByDescending(notification => applications.ContainsKey(notification.LotId))
                    .ThenByDescending(notification => feedback.GetValueOrDefault(notification.LotId) == "like")
                    .ThenByDescending(notification => notification.CreatedAt)
                    .ThenByDescending(notification => notification.Id).First();
                var preferredTender = tenders[preferred.LotId];
                foreach (var tender in relatedTenders.Where(t => includeExpired || t.EndDate >= now))
                {
                    var lotId = tender.Id.ToString(CultureInfo.InvariantCulture);
                    var notification = group.FirstOrDefault(n => n.LotId == lotId && n.MatchScore >= MatchingService.MinimumMatchScore) ?? preferred;
                    var (offer, reason) = MatchingService.GetCurrentLoanDecision(customer, tender);
                    results.Add(notification.ToDto(tender.EnglishTitle ?? preferredTender.EnglishTitle ?? tender.Summary ?? preferredTender.Summary) with
                    {
                        LotId = lotId,
                        SourceUrl = tender.SourceUrl,
                        Deadline = DateOnly.FromDateTime(tender.EndDate),
                        LoanDecisionReason = reason,
                        Offer = offer,
                        Feedback = feedback.GetValueOrDefault(lotId),
                        AppliedAt = applications.TryGetValue(lotId, out var appliedAt) ? appliedAt : null
                    });
                }
            }
            return results.OrderByDescending(match => match.CreatedAt).ThenByDescending(match => match.Id).ToList();
        }

        [HttpPut("{customerId}/tenders/{tenderId:long}/feedback")]
        public async Task<IActionResult> SetFeedback(string customerId, long tenderId, [FromBody] TenderFeedbackRequest request, CancellationToken ct)
        {
            customerId = NormalizeId(customerId);
            if (request.Value is not ("like" or "dislike")) return BadRequest("Feedback must be like or dislike.");
            var comment = request.Comment?.Trim();
            if (comment?.Length > 500) return BadRequest("Feedback comments must not exceed 500 characters.");
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct)
                || !await db.Tenders.AnyAsync(t => t.Id == tenderId && !t.IsDeleted, ct)) return NotFound();
            var feedback = await db.TenderFeedback.FindAsync([customerId, tenderId], ct);
            if (feedback is null)
            {
                feedback = new TenderFeedbackEntity { CustomerId = customerId, TenderId = tenderId, Value = request.Value };
                db.TenderFeedback.Add(feedback);
            }
            feedback.Value = request.Value;
            feedback.Comment = request.Value == "dislike" && !string.IsNullOrEmpty(comment) ? comment : null;
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
            const string simulatedOutcome = "sent";
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var available = await GetMatchesCore(customerId, includeExpired: true, ct);
            if (available.Result is not null) return available.Result;
            var sourceMatch = available.Value!.FirstOrDefault(match => match.LotId == lotId);
            var sourceNotificationId = sourceMatch?.Id;
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TenderApplications" ("CustomerId", "TenderId", "AppliedAt", "SimulatedOutcome")
                SELECT {customerId}, {tenderId}, {now}, {simulatedOutcome}
                WHERE EXISTS (SELECT 1 FROM "Customers" WHERE "CustomerId" = {customerId})
                  AND EXISTS (SELECT 1 FROM "Tenders"
                              WHERE "Id" = {tenderId} AND "IsDeleted" = 0 AND "EndDate" >= {localNow})
                  AND EXISTS (SELECT 1 FROM "Notifications"
                              WHERE "CustomerId" = {customerId} AND "Id" = {sourceNotificationId})
                  AND NOT EXISTS (SELECT 1 FROM "TenderFeedback"
                                  WHERE "CustomerId" = {customerId} AND "TenderId" = {tenderId} AND "Value" = 'dislike')
                ON CONFLICT ("CustomerId", "TenderId") DO NOTHING
                """, ct);
            if (inserted > 0 && sourceMatch is not null
                && !await db.Notifications.AnyAsync(n => n.CustomerId == customerId && n.LotId == lotId, ct))
            {
                var source = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == sourceMatch.Id && n.CustomerId == customerId, ct);
                db.Notifications.Add(new NotificationEntity
                {
                    Id = $"N-{Guid.NewGuid():N}"[..20],
                    CustomerId = customerId,
                    LotId = lotId,
                    CreatedAt = source.CreatedAt,
                    Title = sourceMatch.Title,
                    Body = source.Body,
                    Buyer = source.Buyer,
                    SourceUrl = sourceMatch.SourceUrl,
                    Region = source.Region,
                    StartPrice = source.StartPrice,
                    CurrencyCode = source.CurrencyCode,
                    Deadline = sourceMatch.Deadline,
                    PaymentTermsDays = source.PaymentTermsDays,
                    Requirements = source.Requirements.ToList(),
                    MatchReason = source.MatchReason,
                    MatchScore = source.MatchScore,
                    LoanDecisionReason = source.LoanDecisionReason,
                    WhyText = source.WhyText
                });
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);

            var application = await db.TenderApplications.AsNoTracking()
                .SingleOrDefaultAsync(a => a.CustomerId == customerId && a.TenderId == tenderId, ct);
            if (application is not null)
            {
                var customer = await db.Customers.AsNoTracking().Include(c => c.RiskFeatures)
                    .SingleOrDefaultAsync(c => c.CustomerId == customerId, ct);
                var tender = await db.Tenders.AsNoTracking()
                    .SingleOrDefaultAsync(t => t.Id == tenderId && !t.IsDeleted, ct);
                if (customer is null || tender is null) return NotFound();
                var (offer, reason) = tender.EndDate >= DateTime.UtcNow.AddHours(5)
                    ? MatchingService.GetCurrentLoanDecision(customer, tender)
                    : (null, "This tender is closed, so a loan application is not suggested.");
                return new TenderApplicationDto("applied", DateTime.SpecifyKind(application.AppliedAt, DateTimeKind.Utc), offer, reason, application.SimulatedOutcome is not null);
            }
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct)
                || !await db.Tenders.AnyAsync(t => t.Id == tenderId && !t.IsDeleted, ct)) return NotFound();
            return Conflict(new ProblemDetails { Title = "Only an open, matching tender can be marked for preparation." });
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

    public record TenderFeedbackRequest(string Value, string? Comment = null);
    public record MarkApplicationUpdatesReadRequest(IReadOnlyList<long>? TenderIds);
    public record TenderApplicationDto(string Status, DateTime AppliedAt, LoanOfferDto? Offer, string LoanDecisionReason, bool SimulationScheduled);
    public record ApplicationUpdateDto(long TenderId, string DisplayNo, string TenderTitle, string Outcome, DateTime AvailableAt, bool IsRead);
    public record SavedTenderDiscovery(TenderDto Tender, NotificationDto Match, bool IsOpen, string GroupKey);
}
