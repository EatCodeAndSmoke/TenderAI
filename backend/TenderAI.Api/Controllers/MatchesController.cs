using Amazon.Runtime;
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

        [HttpPost("run")]
        public async Task<ActionResult<IReadOnlyList<MatchDto>>> Run(CancellationToken ct)
        {
            try
            {
                return Ok(await matchingService.RunAsync(ct));
            }
            catch (TenderMatchEvaluationException ex)
            {
                logger.LogError(ex, "AI tender matching returned an invalid assessment.");
                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "AI matching could not be completed. Existing results were kept.");
            }
            catch (AmazonServiceException ex)
            {
                logger.LogError(ex, "Amazon Bedrock failed during tender matching.");
                return Problem(
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "AI matching could not be completed. Existing results were kept.");
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
