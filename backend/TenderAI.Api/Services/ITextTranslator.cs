namespace TenderAI.Api.Services
{
    public interface ITextTranslator
    {
        Task<string> TranslateAsync(string text, CancellationToken ct);
    }
}
