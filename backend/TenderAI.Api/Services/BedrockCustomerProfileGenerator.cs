using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Options;

namespace TenderAI.Api.Services
{
    public sealed class BedrockCustomerProfileGenerator(
        IAmazonBedrockRuntime bedrock,
        IOptions<BedrockOptions> options,
        ILogger<BedrockCustomerProfileGenerator> logger) : ICustomerProfileGenerator
    {
        private static readonly JsonSerializerOptions ResponseOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<CustomerProfileDraft> GenerateAsync(
            string tenderSummary,
            string languageCode,
            bool shouldMatchTender,
            IReadOnlyCollection<string> excludedCompanyNames,
            IReadOnlyCollection<string> excludedIndustryNames,
            IReadOnlyCollection<string> excludedPaymentDescriptions,
            CancellationToken ct)
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                schemaVersion = "messages-v1",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { text = CustomerProfilePrompt.Build(
                            tenderSummary,
                            languageCode,
                            shouldMatchTender,
                            excludedCompanyNames,
                            excludedIndustryNames,
                            excludedPaymentDescriptions) } }
                    }
                },
                inferenceConfig = new
                {
                    max_new_tokens = 500,
                    temperature = 0.6
                }
            });

            try
            {
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
                var text = responseJson.RootElement
                    .GetProperty("output")
                    .GetProperty("message")
                    .GetProperty("content")
                    .EnumerateArray()
                    .Select(block => block.TryGetProperty("text", out var value) ? value.GetString() : null)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException("Amazon Bedrock returned an empty customer profile.");
                }

                return JsonSerializer.Deserialize<CustomerProfileDraft>(UnwrapJson(text), ResponseOptions)
                    ?? throw new InvalidOperationException("Amazon Bedrock returned an empty customer profile.");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(
                    ex,
                    "Amazon Bedrock customer profile generation failed for language {LanguageCode}; match required: {MatchesTender}",
                    languageCode,
                    shouldMatchTender);
                throw new CustomerProfileGenerationException(
                    "Amazon Bedrock couldn't generate a valid customer profile. No customers were saved.",
                    ex);
            }
        }

        private static string UnwrapJson(string response)
        {
            var text = response.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = text.IndexOf('\n');
                var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (firstLineEnd < 0 || lastFence <= firstLineEnd)
                {
                    throw new JsonException("The generated customer profile has an invalid code fence.");
                }

                text = text[(firstLineEnd + 1)..lastFence].Trim();
            }

            return text;
        }
    }
}
