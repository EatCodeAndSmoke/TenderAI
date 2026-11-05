using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Services;

namespace TenderAI.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MatchesController(
        TenderAiDbContext db,
        MatchingService matchingService,
        ILogger<MatchesController> logger) : ControllerBase
    {
        private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<MatchDto>>> GetAll(CancellationToken ct)
        {
            var notifications = await db.Notifications
                .AsNoTracking()
                .OrderByDescending(notification => notification.CreatedAt)
                .ThenBy(notification => notification.CustomerId)
                .ThenBy(notification => notification.LotId)
                .ToListAsync(ct);
            var customers = await db.Customers
                .AsNoTracking().Include(customer => customer.RiskFeatures)
                .ToDictionaryAsync(customer => customer.CustomerId, ct);
            var tenderIds = notifications
                .Select(notification => long.TryParse(notification.LotId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    ? (long?)id
                    : null)
                .Where(id => id.HasValue)
                .Select(id => id.GetValueOrDefault())
                .Distinct()
                .ToList();
            var tenders = (await db.Tenders
                .AsNoTracking()
                .Where(tender => tenderIds.Contains(tender.Id))
                .ToListAsync(ct))
                .ToDictionary(
                    tender => tender.Id.ToString(CultureInfo.InvariantCulture),
                    tender => tender);

            return notifications
                .Where(notification => customers.ContainsKey(notification.CustomerId))
                .Select(notification =>
                {
                    var customer = customers[notification.CustomerId];
                    var tender = tenders.GetValueOrDefault(notification.LotId);
                    var match = notification.ToMatchDto(customer.Name, tender?.EnglishTitle ?? tender?.Summary);
                    if (tender is null) return match;
                    var (offer, reason) = MatchingService.GetCurrentLoanDecision(customer, tender);
                    return match with { Offer = offer, LoanDecisionReason = reason };
                })
                .ToList();
        }

        [HttpPost("run/stream")]
        public Task RunStreaming(CancellationToken ct) => RunCore(null, ct);

        [HttpPost("/api/customers/{customerId}/matches/stream")]
        public async Task RunCustomerStreaming(string customerId, CancellationToken ct)
        {
            customerId = customerId.Trim().ToUpperInvariant();
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct))
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await RunCore(customerId, ct);
        }

        [HttpPost("/api/customers/{customerId}/discover/stream")]
        public async Task Discover(string customerId, [FromQuery] int? count,
            [FromServices] TenderImportService importer, CancellationToken ct)
        {
            customerId = customerId.Trim().ToUpperInvariant();
            var targetCount = count ?? MatchingService.DefaultDiscoveryCount;
            if (targetCount < 1 || targetCount > MatchingService.MaxDiscoveryCount)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new ProblemDetails { Title = "Count must be between 1 and 10." }, ct);
                return;
            }
            if (!await db.Customers.AnyAsync(c => c.CustomerId == customerId, ct))
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache, no-store";
            Response.Headers.Append("X-Accel-Buffering", "no");
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            try
            {
                await foreach (var item in matchingService.DiscoverAsync(customerId, targetCount, importer, ct))
                {
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(item, EventJsonOptions)}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tender discovery failed for customer {CustomerId}.", customerId);
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", message = "Tender search stopped. Check UZEX and Bedrock configuration; completed results were saved." }, EventJsonOptions)}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }

        private async Task RunCore(string? customerId, CancellationToken ct)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache, no-store";
            Response.Headers.Append("X-Accel-Buffering", "no");
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            try
            {
                await foreach (var runEvent in matchingService.RunStreamingAsync(ct, customerId))
                {
                    await Response.WriteAsync(
                        $"data: {JsonSerializer.Serialize(runEvent, EventJsonOptions)}\n\n",
                        ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Streaming tender matching failed.");
                if (!Response.HasStarted)
                {
                    Response.Clear();
                    Response.StatusCode = StatusCodes.Status502BadGateway;
                    Response.ContentType = "application/problem+json";
                    await Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = StatusCodes.Status502BadGateway,
                        Title = "The matching run could not be started. Check the API logs."
                    }, ct);
                    return;
                }

                await Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(new { type = "error", message = $"Matching stopped: {ex.GetBaseException().Message} Results already saved for completed customers were kept." }, EventJsonOptions)}\n\n",
                    ct);
                await Response.Body.FlushAsync(ct);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> Clear(CancellationToken ct)
        {
            using var discoveryLock = await MatchingService.LockDiscoveryAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Notifications.ExecuteDeleteAsync(ct);
            await db.TenderDiscoverySeen.ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
            return NoContent();
        }
    }
}
