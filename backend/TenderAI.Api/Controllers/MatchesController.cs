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
            var customerNames = await db.Customers
                .AsNoTracking()
                .ToDictionaryAsync(customer => customer.CustomerId, customer => customer.Name, ct);

            return notifications
                .Where(notification => customerNames.ContainsKey(notification.CustomerId))
                .Select(notification => notification.ToMatchDto(customerNames[notification.CustomerId]))
                .ToList();
        }

        [HttpPost("run/stream")]
        public async Task RunStreaming(CancellationToken ct)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache, no-store";
            Response.Headers.Append("X-Accel-Buffering", "no");
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            try
            {
                await foreach (var runEvent in matchingService.RunStreamingAsync(ct))
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
                    $"data: {JsonSerializer.Serialize(new { type = "error", message = "The matching run stopped unexpectedly. Results already saved for completed customers were kept." }, EventJsonOptions)}\n\n",
                    ct);
                await Response.Body.FlushAsync(ct);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> Clear(CancellationToken ct)
        {
            await db.Notifications.ExecuteDeleteAsync(ct);
            return NoContent();
        }
    }
}
