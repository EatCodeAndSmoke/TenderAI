using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public interface ITenderSummarizer
    {
        Task<string> SummarizeAsync(TenderEntity tender, CancellationToken ct);
    }
}
