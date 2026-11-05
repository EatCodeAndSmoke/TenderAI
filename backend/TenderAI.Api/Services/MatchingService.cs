using System.Globalization;
using System.Runtime.CompilerServices;
using Amazon.Runtime;
using Microsoft.EntityFrameworkCore;
using TenderAI.Api.Contracts;
using TenderAI.Api.Data;
using TenderAI.Api.Data.Entities;

namespace TenderAI.Api.Services
{
    public sealed class MatchingService(
        TenderAiDbContext db,
        ITenderMatchEvaluator evaluator,
        TimeProvider timeProvider,
        ILogger<MatchingService> logger)
    {
        private static readonly TimeSpan TashkentOffset = TimeSpan.FromHours(5);
        private const int MaxConcurrentCustomerEvaluations = 2;
        public const int DefaultDiscoveryCount = 3;
        public const int MaxDiscoveryCount = 10;
        public const int MinimumMatchScore = 75;
        private const int DiscoveryBatchSize = 5;
        public const int MaxImportedPerSearch = 50;
        private static readonly TimeSpan DiscoveryTimeLimit = TimeSpan.FromSeconds(10);
        private static readonly SemaphoreSlim DiscoveryLock = new(1, 1);

        public static async Task<IDisposable> LockDiscoveryAsync(CancellationToken ct)
        {
            await DiscoveryLock.WaitAsync(ct);
            return new DiscoveryLockReleaser();
        }

        private sealed class DiscoveryLockReleaser : IDisposable
        {
            public void Dispose() => DiscoveryLock.Release();
        }

        public async IAsyncEnumerable<TenderDiscoveryEvent> DiscoverAsync(
            string customerId, int count, TenderImportService importer, [EnumeratorCancellation] CancellationToken ct)
        {
            if (count < 1 || count > MaxDiscoveryCount) throw new ArgumentOutOfRangeException(nameof(count));
            var startedAt = timeProvider.GetTimestamp();
            using (await LockDiscoveryAsync(ct))
            {
                using var timeLimit = new CancellationTokenSource();
                using var searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, timeLimit.Token);
                await using var timer = timeProvider.CreateTimer(
                    static state => ((CancellationTokenSource)state!).Cancel(),
                    timeLimit, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                var now = timeProvider.GetUtcNow().ToOffset(TashkentOffset).DateTime;
                var today = DateOnly.FromDateTime(now);
                var customer = await db.Customers.AsNoTracking().Include(c => c.RiskFeatures).Include(c => c.Payments)
                    .AsSplitQuery().SingleAsync(c => c.CustomerId == customerId, ct);
                var feedback = (await db.TenderFeedback.AsNoTracking().Where(f => f.CustomerId == customerId).ToListAsync(ct))
                    .ToDictionary(f => f.TenderId, f => f.Value);
                var seen = (await db.TenderDiscoverySeen.AsNoTracking().Where(s => s.CustomerId == customerId)
                    .Select(s => s.TenderId).ToListAsync(ct)).ToHashSet();
                var savedLots = await db.Notifications.AsNoTracking().Where(n => n.CustomerId == customerId)
                    .Select(n => n.LotId).ToListAsync(ct);
                foreach (var lot in savedLots)
                    if (long.TryParse(lot, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) seen.Add(id);
                var savedIds = savedLots.Select(lot => long.TryParse(lot, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? (long?)id : null)
                    .OfType<long>().Distinct().ToList();
                var savedTenders = await db.Tenders.AsNoTracking().Include(t => t.Products)
                    .Where(t => savedIds.Contains(t.Id) && !t.IsDeleted).ToListAsync(ct);
                var savedIdentities = savedTenders.Select(TenderIdentity.For).OfType<string>().ToHashSet(StringComparer.Ordinal);
                var stored = await db.Tenders.AsNoTracking().Include(t => t.Products)
                    .Where(t => !t.IsDeleted && t.EndDate >= now)
                    .OrderBy(t => t.EndDate).ThenBy(t => t.Id).ToListAsync(ct);
                var candidates = stored.Where(t => !seen.Contains(t.Id) && feedback.GetValueOrDefault(t.Id) != "dislike").ToList();
                var matchedCount = 0;
                var importedCount = 0;
                var checkedCount = 0;
                var limited = false;
                var timeLimited = false;
                yield return new TenderDiscoveryEvent("started", Count: count);
                if (!customer.Payments.Any(p => p.IsIncoming && p.Date >= today.AddMonths(-12)
                    && p.Date <= today && !string.IsNullOrWhiteSpace(p.PurposeDescription)))
                {
                    yield return new TenderDiscoveryEvent("completed", Count: 0);
                    yield break;
                }
                using var semaphore = new SemaphoreSlim(1);
                while (matchedCount < count)
                {
                    ct.ThrowIfCancellationRequested();
                    if (timeLimit.IsCancellationRequested) { timeLimited = true; break; }
                    if (candidates.Count == 0)
                    {
                        if (importedCount >= MaxImportedPerSearch) { limited = true; break; }
                        yield return new TenderDiscoveryEvent("progress", matchedCount, Imported: importedCount, Checked: checkedCount, Phase: "loading");
                        var loadedCount = 0;
                        await using (var imports = importer.LoadNewStreamingAsync(
                            Math.Min(DiscoveryBatchSize, MaxImportedPerSearch - importedCount), searchCancellation.Token)
                            .GetAsyncEnumerator(searchCancellation.Token))
                        {
                            while (true)
                            {
                                bool movedNext;
                                try
                                {
                                    movedNext = await imports.MoveNextAsync();
                                }
                                catch (OperationCanceledException) when (timeLimit.IsCancellationRequested && !ct.IsCancellationRequested)
                                {
                                    timeLimited = true;
                                    break;
                                }
                                if (!movedNext) break;
                                var tender = imports.Current;
                                loadedCount++;
                                importedCount++;
                                if (!tender.IsDeleted && tender.EndDate >= now && !seen.Contains(tender.Id)
                                    && feedback.GetValueOrDefault(tender.Id) != "dislike")
                                    candidates.Add(tender);
                                yield return new TenderDiscoveryEvent("progress", matchedCount, Imported: importedCount, Checked: checkedCount, Phase: "loading");
                                ct.ThrowIfCancellationRequested();
                                if (timeLimit.IsCancellationRequested) { timeLimited = true; break; }
                            }
                        }
                        if (timeLimited) break;
                        if (loadedCount == 0) break;
                        if (candidates.Count == 0) continue;
                    }
                    var batch = candidates.Take(DiscoveryBatchSize).ToList();
                    candidates.RemoveRange(0, batch.Count);
                    yield return new TenderDiscoveryEvent("progress", matchedCount, Imported: importedCount, Checked: checkedCount, Phase: "matching");
                    CustomerEvaluationResult evaluation;
                    try
                    {
                        evaluation = await EvaluateCustomerAsync(customer, batch.Select(ToTenderEvidence).ToList(),
                            today.AddMonths(-12), today, semaphore, searchCancellation.Token);
                    }
                    catch (OperationCanceledException) when (timeLimit.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        timeLimited = true;
                        break;
                    }
                    if (evaluation.Error is not null)
                        throw new TenderMatchEvaluationException("The tender search could not be completed.", evaluation.Error);
                    checkedCount += batch.Count;
                    yield return new TenderDiscoveryEvent("progress", matchedCount, Imported: importedCount, Checked: checkedCount, Phase: "matching");
                    var matches = evaluation.Matches!.ToDictionary(match => match.TenderId, StringComparer.Ordinal);
                    foreach (var tender in batch)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (timeLimit.IsCancellationRequested) { timeLimited = true; break; }
                        matches.TryGetValue(tender.Id.ToString(CultureInfo.InvariantCulture), out var match);
                        var identity = TenderIdentity.For(tender);
                        if (identity is not null && savedIdentities.Contains(identity)) match = null;
                        if (match is not null && (string.IsNullOrWhiteSpace(tender.Summary) || string.IsNullOrWhiteSpace(tender.EnglishTitle)))
                        {
                            yield return new TenderDiscoveryEvent("progress", matchedCount, Imported: importedCount, Checked: checkedCount, Phase: "summarizing");
                            try
                            {
                                await importer.RefreshSummaryAsync(tender, searchCancellation.Token);
                            }
                            catch (OperationCanceledException) when (timeLimit.IsCancellationRequested && !ct.IsCancellationRequested)
                            {
                                timeLimited = true;
                            }
                            if (timeLimited) break;
                            if (string.IsNullOrWhiteSpace(tender.Summary) || string.IsNullOrWhiteSpace(tender.EnglishTitle))
                                throw new TenderMatchEvaluationException("A matching tender could not be summarized. Check the Bedrock configuration.");
                        }
                        var notification = match is null ? null : CreateNotification(customer, tender, match.Reason, match.MatchScore, timeProvider.GetUtcNow().UtcDateTime);
                        string? currentFeedback;
                        DateTime? appliedAt;
                        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
                        {
                            if (!await db.Tenders.AsNoTracking().AnyAsync(t => t.Id == tender.Id && !t.IsDeleted, ct))
                                continue;
                            currentFeedback = await db.TenderFeedback.AsNoTracking()
                                .Where(f => f.CustomerId == customerId && f.TenderId == tender.Id)
                                .Select(f => f.Value).SingleOrDefaultAsync(ct);
                            if (currentFeedback == "dislike") continue;
                            var marker = new TenderDiscoverySeenEntity { CustomerId = customerId, TenderId = tender.Id };
                            db.TenderDiscoverySeen.Add(marker);
                            appliedAt = await db.TenderApplications.AsNoTracking()
                                .Where(a => a.CustomerId == customerId && a.TenderId == tender.Id)
                                .Select(a => (DateTime?)a.AppliedAt).SingleOrDefaultAsync(ct);
                            if (notification is not null && await db.Notifications.AsNoTracking()
                                .AnyAsync(n => n.CustomerId == customerId && n.LotId == match!.TenderId, ct))
                                notification = null;
                            if (notification is not null) db.Notifications.Add(notification);
                            await db.SaveChangesAsync(ct);
                            await transaction.CommitAsync(ct);
                            db.Entry(marker).State = EntityState.Detached;
                        }
                        if (notification is null) continue;
                        if (identity is not null) savedIdentities.Add(identity);
                        matchedCount++;
                        if (matchedCount == 1 && matchedCount < count)
                        {
                            var remaining = DiscoveryTimeLimit - timeProvider.GetElapsedTime(startedAt);
                            if (remaining <= TimeSpan.Zero) timeLimited = true;
                            else timer.Change(remaining, Timeout.InfiniteTimeSpan);
                        }
                        yield return new TenderDiscoveryEvent("match", matchedCount,
                            tender.ToDto(), notification.ToDto(tender.EnglishTitle) with
                            {
                                Feedback = currentFeedback,
                                AppliedAt = appliedAt.HasValue ? DateTime.SpecifyKind(appliedAt.Value, DateTimeKind.Utc) : null
                            }, Imported: importedCount, Checked: checkedCount);
                        if (matchedCount == count || timeLimited) break;
                    }
                    if (timeLimited) break;
                    yield return new TenderDiscoveryEvent("progress", matchedCount, Imported: importedCount, Checked: checkedCount, Phase: "matching");
                }
                ct.ThrowIfCancellationRequested();
                yield return new TenderDiscoveryEvent("completed", matchedCount, Imported: importedCount, Limited: limited, Checked: checkedCount, TimeLimited: timeLimited);
            }
        }

        public async IAsyncEnumerable<MatchRunEvent> RunStreamingAsync(
            [EnumeratorCancellation] CancellationToken ct, string? customerId = null)
        {
            var now = timeProvider.GetUtcNow().ToOffset(TashkentOffset).DateTime;
            var today = DateOnly.FromDateTime(now);
            var paymentCutoff = today.AddMonths(-12);

            var query = db.Customers
                .AsNoTracking()
                .Include(customer => customer.RiskFeatures)
                .Include(customer => customer.Payments)
                .AsSplitQuery();
            if (customerId is not null) query = query.Where(customer => customer.CustomerId == customerId);
            var customers = await query
                .OrderBy(customer => customer.CustomerId)
                .ToListAsync(ct);

            var storedTenders = await db.Tenders
                .AsNoTracking()
                .Include(tender => tender.Products)
                .Where(tender => !tender.IsDeleted && tender.EndDate >= now)
                .OrderBy(tender => tender.EndDate)
                .ThenBy(tender => tender.Id)
                .ToListAsync(ct);
            var tenders = storedTenders.Select(ToTenderEvidence).ToList();
            var tendersById = storedTenders.ToDictionary(
                tender => tender.Id.ToString(CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

            yield return new MatchRunEvent("started", TotalCustomers: customers.Count, TotalTenders: tenders.Count);

            using var semaphore = new SemaphoreSlim(MaxConcurrentCustomerEvaluations);
            var pending = customers
                .Select(customer => EvaluateCustomerAsync(customer, tenders, paymentCutoff, today, semaphore, ct))
                .ToHashSet();
            var completedCount = 0;
            var failedCount = 0;

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var finishedTask = await Task.WhenAny(pending);
                pending.Remove(finishedTask);
                var evaluation = await finishedTask;
                completedCount++;

                if (evaluation.Error is not null)
                {
                    failedCount++;
                    yield return new MatchRunEvent(
                        "customer-error",
                        TotalCustomers: customers.Count,
                        TotalTenders: tenders.Count,
                        CompletedCustomers: completedCount,
                        FailedCustomers: failedCount,
                        CustomerId: evaluation.Customer.CustomerId,
                        CustomerName: evaluation.Customer.Name,
                        Error: DescribeError(evaluation.Error));
                    continue;
                }

                var timestamp = timeProvider.GetUtcNow().UtcDateTime;
                var notifications = evaluation.Matches!
                    .Select(match => CreateNotification(
                        evaluation.Customer,
                        tendersById[match.TenderId],
                        match.Reason,
                        match.MatchScore,
                        timestamp))
                    .ToList();

                Exception? persistenceError = null;
                try
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    var notificationLots = notifications.Select(n => n.LotId).ToArray();
                    await db.Notifications
                        .Where(notification => notification.CustomerId == evaluation.Customer.CustomerId
                            && notificationLots.Contains(notification.LotId))
                        .ExecuteDeleteAsync(ct);
                    db.Notifications.AddRange(notifications);
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    logger.LogError(ex, "Saving matching results failed for customer {CustomerId}.", evaluation.Customer.CustomerId);
                    db.ChangeTracker.Clear();
                    persistenceError = ex;
                }

                if (persistenceError is not null)
                {
                    yield return new MatchRunEvent(
                        "customer-error",
                        TotalCustomers: customers.Count,
                        TotalTenders: tenders.Count,
                        CompletedCustomers: completedCount,
                        FailedCustomers: failedCount,
                        CustomerId: evaluation.Customer.CustomerId,
                        CustomerName: evaluation.Customer.Name,
                        Error: "The result could not be saved; this customer's previous results were kept.");
                    continue;
                }

                var matchDtos = notifications
                    .Select(notification => notification.ToMatchDto(evaluation.Customer.Name))
                    .ToList();
                yield return new MatchRunEvent(
                    "customer-completed",
                    TotalCustomers: customers.Count,
                    TotalTenders: tenders.Count,
                    CompletedCustomers: completedCount,
                    FailedCustomers: failedCount,
                    CustomerId: evaluation.Customer.CustomerId,
                    CustomerName: evaluation.Customer.Name,
                    Matches: matchDtos);
            }

            yield return new MatchRunEvent(
                "completed",
                TotalCustomers: customers.Count,
                TotalTenders: tenders.Count,
                CompletedCustomers: completedCount,
                FailedCustomers: failedCount);
        }

        private async Task<CustomerEvaluationResult> EvaluateCustomerAsync(
            CustomerEntity customer,
            IReadOnlyCollection<TenderMatchEvidence> tenders,
            DateOnly paymentCutoff,
            DateOnly today,
            SemaphoreSlim semaphore,
            CancellationToken ct)
        {
            var acquired = false;
            try
            {
                if (tenders.Count == 0) return new CustomerEvaluationResult(customer, []);
                await semaphore.WaitAsync(ct);
                acquired = true;
                var industryCode = customer.IndustryCode.Trim();
                var industryName = customer.IndustryName.Trim();
                var payments = customer.Payments
                    .Where(payment => payment.IsIncoming
                        && payment.Date >= paymentCutoff
                        && payment.Date <= today
                        && !string.IsNullOrWhiteSpace(payment.PurposeDescription))
                    .OrderByDescending(payment => payment.Date)
                    .Take(30)
                    .Select(payment => new IncomingPaymentEvidence(payment.Date, payment.PurposeDescription))
                    .ToList();
                var evidence = new CustomerMatchEvidence(
                    customer.CustomerId,
                    industryCode,
                    industryName,
                    customer.RegionName,
                    customer.DistrictName,
                    payments);
                var matches = await evaluator.EvaluateCustomerAsync(evidence, tenders, ct);
                return new CustomerEvaluationResult(customer, ValidateMatches(matches, tenders));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tender matching failed for customer {CustomerId}.", customer.CustomerId);
                return new CustomerEvaluationResult(customer, Error: ex);
            }
            finally
            {
                if (acquired)
                {
                    semaphore.Release();
                }
            }
        }

        private static IReadOnlyList<TenderCandidateMatch> ValidateMatches(
            IReadOnlyList<TenderCandidateMatch> matches,
            IReadOnlyCollection<TenderMatchEvidence> tenders)
        {
            var knownTenderIds = tenders.Select(tender => tender.TenderId).ToHashSet(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var match in matches)
            {
                if (!knownTenderIds.Contains(match.TenderId)
                    || !ids.Add(match.TenderId)
                    || string.IsNullOrWhiteSpace(match.Reason)
                    || match.MatchScore is < 1 or > 100
                    || match.Reason.Length > TenderMatchPrompt.MaximumReasonCharacters
                    || match.Reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > TenderMatchPrompt.MaximumReasonWords)
                {
                    throw new TenderMatchEvaluationException("The matching response contained invalid tender results.");
                }
            }

            return matches.Where(match => match.MatchScore >= MinimumMatchScore).ToList();
        }

        private NotificationEntity CreateNotification(
            CustomerEntity customer,
            TenderEntity tender,
            string matchReason,
            int matchScore,
            DateTime timestamp)
        {
            var (offer, loanReason) = GetLoanDecision(customer, tender);
            return new NotificationEntity
            {
                Id = $"N-{Guid.NewGuid():N}"[..20],
                CustomerId = customer.CustomerId,
                LotId = tender.Id.ToString(CultureInfo.InvariantCulture),
                CreatedAt = timestamp,
                Title = tender.EnglishTitle ?? tender.Summary ?? tender.Name,
                Body = GetTenderBody(tender),
                Buyer = tender.BuyerName,
                SourceUrl = tender.SourceUrl,
                Region = tender.DeliveryRegion ?? tender.RegionName ?? "",
                StartPrice = tender.StartPrice,
                CurrencyCode = tender.CurrencyCode,
                Deadline = DateOnly.FromDateTime(tender.EndDate),
                PaymentTermsDays = tender.PaymentTermDays ?? 0,
                Requirements = GetRequirements(tender),
                MatchReason = matchReason,
                MatchScore = matchScore,
                LoanDecisionReason = loanReason,
                WhyText = matchReason,
                Offer = offer
            };
        }

        private static (LoanOfferEntity? Offer, string Reason) GetLoanDecision(
            CustomerEntity customer,
            TenderEntity tender)
        {
            var risk = customer.RiskFeatures;
            var limit = risk?.PreApprovedLimit;
            if (limit is not > 0)
            {
                return (null, "No pre-approved limit is on record, so a loan application is not suggested.");
            }

            if (!string.Equals(tender.CurrencyCode, "UZS", StringComparison.OrdinalIgnoreCase))
            {
                return (null, "This tender is not in UZS, so a loan application is not suggested.");
            }

            if (limit.Value < tender.StartPrice * 0.5m)
            {
                return (null, "The pre-approved limit is below 50% of the tender amount.");
            }

            if (risk!.OverdueAmount > 0 || risk.MaxOverdueDays12m > 0)
            {
                return (null, "Payment arrears are recorded in the last 12 months, so a loan application is not suggested.");
            }

            var financing = ToAmount(Math.Min(tender.StartPrice, limit.Value));
            var guarantee = 0L;
            if (tender.BankGuaranteeAccepted && tender.DepositPercent is > 0)
            {
                var requestedGuarantee = ToAmount(tender.StartPrice * tender.DepositPercent.Value / 100m);
                if (requestedGuarantee <= limit.Value)
                {
                    guarantee = requestedGuarantee;
                }
            }

            if (financing == 0)
            {
                return (null, "No eligible application amount is available for this tender.");
            }

            var products = new List<string> { $"financing up to {FormatAmount(financing)} so'm" };
            if (guarantee > 0)
            {
                products.Add($"a bid guarantee of {FormatAmount(guarantee)} so'm");
            }

            return (
                new LoanOfferEntity
                {
                    Financing = financing,
                    Guarantee = guarantee,
                    PreApprovedLimit = limit.Value
                },
                $"Limit covers at least 50% of the tender and no arrears are recorded; you may apply for {string.Join(" and ", products)}. Final approval requires bank review.");
        }

        internal static (LoanOfferDto? Offer, string Reason) GetCurrentLoanDecision(CustomerEntity customer, TenderEntity tender)
        {
            var (offer, reason) = GetLoanDecision(customer, tender);
            return (offer is null ? null : new LoanOfferDto(offer.Financing, offer.Guarantee, offer.PreApprovedLimit), reason);
        }

        private static TenderMatchEvidence ToTenderEvidence(TenderEntity tender)
        {
            var products = tender.Products
                .Where(product => !string.IsNullOrWhiteSpace(product.ProductName))
                .OrderBy(product => product.OrderNum)
                .Select(product => new TenderProductEvidence(
                    product.ProductCode,
                    product.CategoryName,
                    product.ProductName,
                    product.Description,
                    product.TechnicalSpecifications))
                .ToList();
            var description = string.Join(
                "\n",
                new[] { tender.Summary, tender.Name, tender.DetailedName, tender.Description, tender.AdditionalDescription }
                    .Concat(products.SelectMany(product => new[]
                    {
                        product.ProductName,
                        product.CategoryName,
                        product.Description,
                        product.TechnicalSpecifications
                    }))
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            return new TenderMatchEvidence(
                tender.Id.ToString(CultureInfo.InvariantCulture),
                description,
                tender.DeliveryRegion ?? tender.RegionName,
                tender.DeliveryDistrict ?? tender.DistrictName,
                products);
        }

        private static long ToAmount(decimal amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            return decimal.ToInt64(decimal.Truncate(Math.Min(amount, long.MaxValue)));
        }

        private static string FormatAmount(long amount) =>
            amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

        private static string DescribeError(Exception exception)
        {
            if (exception is AmazonServiceException { ErrorCode: { } errorCode })
            {
                return errorCode.Equals("ThrottlingException", StringComparison.OrdinalIgnoreCase)
                    || errorCode.Equals("TooManyRequestsException", StringComparison.OrdinalIgnoreCase)
                    ? "Bedrock throttled this customer request after retries. Other customers continue; retry this run later."
                    : $"Bedrock request failed ({errorCode}). Other customers continue.";
            }

            if (exception is TenderMatchEvaluationException)
            {
                return "Bedrock returned an invalid matching response. Other customers continue.";
            }

            return "This customer could not be evaluated. Other customers continue.";
        }

        private static string GetTenderBody(TenderEntity tender)
        {
            if (!string.IsNullOrWhiteSpace(tender.Summary))
            {
                return tender.Summary;
            }

            if (!string.IsNullOrWhiteSpace(tender.Description))
            {
                return tender.Description;
            }

            if (!string.IsNullOrWhiteSpace(tender.DetailedName))
            {
                return tender.DetailedName;
            }

            return tender.Name;
        }

        private static List<string> GetRequirements(TenderEntity tender) =>
            tender.Products.Select(product => product.ProductName)
                .Concat(tender.BuyerRequirements)
                .Concat(tender.Requirements)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();

        private sealed record CustomerEvaluationResult(
            CustomerEntity Customer,
            IReadOnlyList<TenderCandidateMatch>? Matches = null,
            Exception? Error = null);
    }

    public sealed record MatchRunEvent(
        string Type,
        int TotalCustomers = 0,
        int TotalTenders = 0,
        int CompletedCustomers = 0,
        int FailedCustomers = 0,
        string? CustomerId = null,
        string? CustomerName = null,
        IReadOnlyList<MatchDto>? Matches = null,
        string? Error = null);

    public sealed record TenderDiscoveryEvent(
        string Type, int Count = 0, TenderDto? Tender = null, NotificationDto? Match = null,
        int Imported = 0, bool Limited = false, int Checked = 0, string? Phase = null, bool TimeLimited = false);
}
