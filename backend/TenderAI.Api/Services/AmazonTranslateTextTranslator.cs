using Amazon.Translate;
using Amazon.Translate.Model;
using System.Text;

namespace TenderAI.Api.Services
{
    public sealed class AmazonTranslateTextTranslator : ITextTranslator, IDisposable
    {
        private readonly Lazy<IAmazonTranslate> _client = new(() => new AmazonTranslateClient());

        public async Task<string> TranslateAsync(string text, CancellationToken ct)
        {
            var translated = new StringBuilder();
            foreach (var chunk in TranslateTextChunker.Split(text))
            {
                var response = await _client.Value.TranslateTextAsync(new TranslateTextRequest
                {
                    Text = chunk,
                    SourceLanguageCode = "uz",
                    TargetLanguageCode = "en"
                }, ct);
                translated.Append(response.TranslatedText);
            }

            return translated.ToString();
        }

        public void Dispose()
        {
            if (_client.IsValueCreated)
            {
                _client.Value.Dispose();
            }
        }
    }
}
