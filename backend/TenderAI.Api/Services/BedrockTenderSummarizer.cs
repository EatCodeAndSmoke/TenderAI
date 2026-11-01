using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Options;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class BedrockTenderSummarizer(
        IAmazonBedrockRuntime bedrock,
        IOptions<BedrockOptions> options) : ITenderSummarizer
    {
        public async Task<string> SummarizeAsync(TenderEntity tender, CancellationToken ct)
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                schemaVersion = "messages-v1",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { text = TenderSummaryPrompt.Build(tender) } }
                    }
                },
                inferenceConfig = new
                {
                    max_new_tokens = 220,
                    temperature = 0.1
                }
            });

            using var requestBodyStream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
            var response = await bedrock.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = options.Value.ModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = requestBodyStream
            }, ct);

            using var responseBody = response.Body;
            using var responseJson = await JsonDocument.ParseAsync(responseBody, cancellationToken: ct);
            var summary = responseJson.RootElement
                .GetProperty("output")
                .GetProperty("message")
                .GetProperty("content")
                .EnumerateArray()
                .Select(block => block.TryGetProperty("text", out var text) ? text.GetString() : null)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
                ?.Trim();

            return string.IsNullOrWhiteSpace(summary)
                ? throw new InvalidOperationException("Amazon Bedrock returned an empty tender summary.")
                : summary;
        }
    }
}
