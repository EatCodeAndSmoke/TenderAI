using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;

namespace TenderAI.Api.Services
{
    public sealed class BedrockTenderMatchEvaluator(
        IAmazonBedrockRuntime bedrock,
        IOptions<BedrockOptions> options,
        ILogger<BedrockTenderMatchEvaluator> logger) : ITenderMatchEvaluator
    {
        private const int MaxInvocationAttempts = 4;
        private static readonly HashSet<string> ThrottleErrorCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ThrottlingException",
            "TooManyRequestsException"
        };
        private readonly SemaphoreSlim invocationGate = new(2, 2);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<TenderMatchEvaluation> EvaluateAsync(
            string industryCode,
            string industryName,
            IReadOnlyCollection<TenderProductEvidence> products,
            IReadOnlyCollection<IncomingPaymentEvidence> incomingPayments,
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
                        content = new[]
                        {
                            new
                            {
                                text = TenderMatchPrompt.Build(
                                    industryCode,
                                    industryName,
                                    products,
                                    incomingPayments)
                            }
                        }
                    }
                },
                inferenceConfig = new
                {
                    max_new_tokens = 220,
                    temperature = 0
                }
            });

            var response = await InvokeWithThrottleRetryAsync(requestBody, ct);

            JsonDocument responseJson;
            using (var responseBody = response.Body)
            {
                try
                {
                    responseJson = await JsonDocument.ParseAsync(responseBody, cancellationToken: ct);
                }
                catch (JsonException ex)
                {
                    throw new TenderMatchEvaluationException("Amazon Bedrock returned invalid response JSON.", ex);
                }
            }

            string? responseText;
            using (responseJson)
            {
                var root = responseJson.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("output", out var output)
                    || output.ValueKind != JsonValueKind.Object
                    || !output.TryGetProperty("message", out var message)
                    || message.ValueKind != JsonValueKind.Object
                    || !message.TryGetProperty("content", out var content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    throw new TenderMatchEvaluationException("Amazon Bedrock returned an invalid response structure.");
                }

                responseText = content.EnumerateArray()
                    .Select(block => block.ValueKind == JsonValueKind.Object
                        && block.TryGetProperty("text", out var text) ? text.GetString() : null)
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            }

            if (string.IsNullOrWhiteSpace(responseText))
            {
                throw new TenderMatchEvaluationException("Amazon Bedrock returned an empty matching assessment.");
            }

            TenderMatchAssessmentDraft? draft;
            try
            {
                draft = JsonSerializer.Deserialize<TenderMatchAssessmentDraft>(
                    RemoveMarkdownFence(responseText),
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new TenderMatchEvaluationException("Amazon Bedrock returned invalid matching JSON.", ex);
            }

            if (draft?.OkedSupportsProducts is null || draft.IncomingPaymentsSupportProducts is null)
            {
                throw new TenderMatchEvaluationException("Amazon Bedrock omitted required matching evidence.");
            }

            var reason = draft.Reason?.Trim() ?? "";
            if (draft.OkedSupportsProducts.Value
                && draft.IncomingPaymentsSupportProducts.Value
                && (reason.Length == 0
                    || reason.Length > 240
                    || reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 25))
            {
                throw new TenderMatchEvaluationException("Amazon Bedrock returned an invalid matching explanation.");
            }

            return new TenderMatchEvaluation(
                draft.OkedSupportsProducts.Value,
                draft.IncomingPaymentsSupportProducts.Value,
                reason);
        }

        private static string RemoveMarkdownFence(string text)
        {
            var result = text.Trim();
            if (!result.StartsWith("```", StringComparison.Ordinal))
            {
                return result;
            }

            var firstLineEnd = result.IndexOf('\n');
            var lastFence = result.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd < 0 || lastFence <= firstLineEnd)
            {
                return result;
            }

            return result[(firstLineEnd + 1)..lastFence].Trim();
        }

        private async Task<InvokeModelResponse> InvokeWithThrottleRetryAsync(string requestBody, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                await invocationGate.WaitAsync(ct);
                try
                {
                    using var requestBodyStream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
                    return await bedrock.InvokeModelAsync(new InvokeModelRequest
                    {
                        ModelId = options.Value.ModelId,
                        ContentType = "application/json",
                        Accept = "application/json",
                        Body = requestBodyStream
                    }, ct);
                }
                catch (AmazonServiceException ex) when (IsThrottling(ex) && attempt < MaxInvocationAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(
                        500 * (1 << (attempt - 1)) + Random.Shared.Next(0, 251));
                    logger.LogWarning(
                        ex,
                        "Bedrock throttled a tender-match request; retrying attempt {Attempt} of {MaxAttempts} after {DelayMs} ms.",
                        attempt + 1,
                        MaxInvocationAttempts,
                        delay.TotalMilliseconds);
                    await Task.Delay(delay, ct);
                }
                finally
                {
                    invocationGate.Release();
                }
            }
        }

        private static bool IsThrottling(AmazonServiceException exception) =>
            exception.StatusCode == HttpStatusCode.TooManyRequests
            || (!string.IsNullOrWhiteSpace(exception.ErrorCode)
                && ThrottleErrorCodes.Contains(exception.ErrorCode));

        private sealed class TenderMatchAssessmentDraft
        {
            public bool? OkedSupportsProducts { get; set; }
            public bool? IncomingPaymentsSupportProducts { get; set; }
            public string? Reason { get; set; }
        }
    }
}
