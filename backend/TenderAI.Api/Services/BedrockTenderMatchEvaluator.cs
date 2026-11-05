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
        PromptSettings prompts,
        ILogger<BedrockTenderMatchEvaluator> logger) : ITenderMatchEvaluator
    {
        private const int MaxInvocationAttempts = 4;
        private const int MaxScoreAttempts = 3;
        private const string ScoreRetryInstructions = """

            The previous response had a missing, non-integer, or out-of-range matchScore. Reassess the supplied evidence
            and return the complete JSON object again. Every item in "matches" must contain "tenderId",
            "reason", and "matchScore". The matchScore must be a JSON integer between 75 and 100, not a
            string or percentage. Use only supplied tender IDs. If no tender qualifies, return {"matches":[]}.
            """;
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

        public async Task<IReadOnlyList<TenderCandidateMatch>> EvaluateCustomerAsync(
            CustomerMatchEvidence customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders,
            CancellationToken ct)
        {
            var instructions = await prompts.GetInstructionsAsync(PromptDefinitions.Matching, ct);
            var prompt = TenderMatchPrompt.Build(customer, tenders, instructions);
            for (var attempt = 1; ; attempt++)
            {
                var retryPrompt = prompt + ScoreRetryInstructions;
                if (attempt > 2)
                    retryPrompt += "\nBefore responding, verify that every returned match has its own numeric integer matchScore.";
                var requestBody = CreateRequestBody(attempt == 1 ? prompt : retryPrompt);
                var response = await InvokeWithThrottleRetryAsync(requestBody, ct);
                try
                {
                    return await ReadMatchesAsync(response, tenders, ct);
                }
                catch (InvalidMatchScoreException ex) when (attempt < MaxScoreAttempts)
                {
                    logger.LogWarning(
                        "Bedrock returned {ScoreProblem}; retrying customer matching attempt {Attempt} of {MaxAttempts}.",
                        ex.Message, attempt + 1, MaxScoreAttempts);
                }
                catch (InvalidMatchScoreException ex)
                {
                    throw new TenderMatchEvaluationException(
                        $"Amazon Bedrock returned an invalid matching score after {MaxScoreAttempts} attempts.", ex);
                }
            }
        }

        private static string CreateRequestBody(string prompt) =>
            JsonSerializer.Serialize(new
            {
                schemaVersion = "messages-v1",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                inferenceConfig = new
                {
                    max_new_tokens = 4096,
                    temperature = 0
                }
            });

        private static async Task<IReadOnlyList<TenderCandidateMatch>> ReadMatchesAsync(
            InvokeModelResponse response, IReadOnlyCollection<TenderMatchEvidence> tenders, CancellationToken ct)
        {
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

            CustomerMatchDraft? draft;
            try
            {
                draft = JsonSerializer.Deserialize<CustomerMatchDraft>(
                    RemoveMarkdownFence(responseText),
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new TenderMatchEvaluationException("Amazon Bedrock returned invalid matching JSON.", ex);
            }

            if (draft?.Matches is null)
            {
                throw new TenderMatchEvaluationException("Amazon Bedrock omitted the required matches list.");
            }

            var knownTenderIds = tenders.Select(tender => tender.TenderId).ToHashSet(StringComparer.Ordinal);
            var result = new List<TenderCandidateMatch>(draft.Matches.Count);
            var returnedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in draft.Matches)
            {
                var tenderId = match.TenderId?.Trim();
                var reason = match.Reason?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(tenderId)
                    || !knownTenderIds.Contains(tenderId)
                    || !returnedIds.Add(tenderId))
                {
                    throw new TenderMatchEvaluationException("Amazon Bedrock returned an unknown or duplicate tender ID.");
                }

                if (reason.Length == 0
                    || reason.Length > TenderMatchPrompt.MaximumReasonCharacters
                    || reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > TenderMatchPrompt.MaximumReasonWords)
                {
                    throw new TenderMatchEvaluationException("Amazon Bedrock returned an invalid matching explanation.");
                }

                if (match.MatchScore is null || match.MatchScore.Value.ValueKind == JsonValueKind.Null)
                    throw new InvalidMatchScoreException("a missing matching score");
                var score = match.MatchScore.Value;
                if (score.ValueKind != JsonValueKind.Number || !score.TryGetInt32(out var matchScore))
                    throw new InvalidMatchScoreException("a non-integer matching score");
                if (matchScore is < 1 or > 100)
                    throw new InvalidMatchScoreException("an out-of-range matching score");

                result.Add(new TenderCandidateMatch(tenderId, reason, matchScore));
            }

            return result;
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
                        "Bedrock throttled a customer matching request; retrying attempt {Attempt} of {MaxAttempts} after {DelayMs} ms.",
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

        private sealed class CustomerMatchDraft
        {
            public List<TenderCandidateMatchDraft>? Matches { get; set; }
        }

        private sealed class TenderCandidateMatchDraft
        {
            public string? TenderId { get; set; }
            public string? Reason { get; set; }
            public JsonElement? MatchScore { get; set; }
        }

        private sealed class InvalidMatchScoreException(string message) : Exception(message);
    }
}
