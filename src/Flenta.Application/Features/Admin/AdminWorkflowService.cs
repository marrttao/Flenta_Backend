using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Admin;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Finance;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;
namespace Flenta.Application.Features.Admin;

public sealed record AdminAccount(Guid Id, string Currency, decimal Balance, string? Iban, bool Blocked);
public sealed record AdminClientDetail(AdminClient Client, string Phone, bool Blocked, bool CardsFrozen, IReadOnlyList<AdminAccount> Accounts, IReadOnlyList<AdminAuditResponse> Audit, Flenta.Application.Abstractions.Kyc.KycIdentityData? Identity = null);
public sealed record AdminAuditResponse(Guid Id, Guid ActorId, string Action, string Reason, Guid? TransactionId, DateTimeOffset CreatedAt);
public sealed record AdminTransactionPage(IReadOnlyList<AdminTransaction> Items, int Total, int Page, DateOnly? Date = null, int TransactionsForDay = 0, int CompletedForDay = 0, int AttentionForDay = 0);
public sealed record AdminKycItem(Guid Id, Guid OwnerId, string DisplayName, string? Email, string Status, string Provider, int Attempt, bool Sandbox, DateTimeOffset CreatedAt, bool? ManualDecision, string? TaxNumber = null);
public sealed record AdminKycPage(IReadOnlyList<AdminKycItem> Items, int Total, int Page, int AwaitingDecision = 0, int InReview = 0, int ApprovedToday = 0);
public sealed record AdminKycEvidence(Guid VerificationId, Guid OwnerId, string? ProviderSessionId, Flenta.Application.Abstractions.Kyc.KycIdentityData? Identity, Flenta.Application.Abstractions.Kyc.KycDocumentEvidence? Documents);
public sealed class AdminWorkflowService(IAdminWorkflowRepository repository, IBankingRepository bank, IBankingUnitOfWork work,
    ICurrentUser user, IIdentityProvider identity, IClock clock, AdminService access, Flenta.Application.Abstractions.Kyc.IKycProvider provider, Flenta.Application.Abstractions.Kyc.IKycRepository identityRepository, Flenta.Application.Abstractions.Kyc.IIdentityVault identityVault)
{
    private async Task<Flenta.Application.Abstractions.Kyc.KycSessionSnapshot> BoundSessionAsync(KycVerification verification, CancellationToken ct)
    {
        var configuration = provider.Configuration;
        if (verification.ProviderSessionId is null || !verification.Matches(configuration.Name, configuration.ConfigurationId, configuration.Sandbox))
            throw new ApplicationFailureException(FailureKind.Conflict, "kyc.configuration_changed", "This verification does not match the active provider configuration.");
        var session = await provider.GetSessionAsync(verification.ProviderSessionId, ct);
        if (session.SessionId != verification.ProviderSessionId || session.ExternalUserId != verification.ExternalUserId
            || session.ConfigurationId != verification.ConfigurationId || session.Sandbox != verification.Sandbox)
            throw new ApplicationFailureException(FailureKind.Conflict, "kyc.provider_mismatch", "The evidence does not match this verification.");
        return session;
    }
    public async Task<AdminKycEvidence> EvidenceAsync(Guid id, CancellationToken ct)
    {
        access.RequireAccess();
        var verification = await repository.VerificationAsync(id, ct) ?? throw new ApplicationFailureException(FailureKind.NotFound, "kyc.not_found", "Verification not found.");
        var session = await BoundSessionAsync(verification, ct);
        return new(verification.Id, verification.OwnerId, verification.ProviderSessionId, session.Identity, session.Evidence);
    }
    public Task<AdminClientDetail> ClientAsync(Guid id, CancellationToken ct) { access.RequireAccess(); return repository.ClientAsync(id, ct); }
    public Task<AdminTransactionPage> TransactionsAsync(string? query, string? status, int page, CancellationToken ct, DateOnly? date = null, string? kind = null)
    {
        access.RequireAccess(); ValidatePage(page); var q = query?.Trim() ?? "";
        if (q.Length > 254 || q.Any(char.IsControl) || status is not (null or "" or "Completed" or "Pending" or "Rejected")) throw new DomainValidationException("admin.invalid_query", "Invalid transaction filter.");
        if (date is { } day && (day.Year < 1970 || day > DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)) || !string.IsNullOrEmpty(kind) && (!Enum.TryParse<TransactionKind>(kind, out var parsed) || !Enum.IsDefined(parsed)))
            throw new DomainValidationException("admin.invalid_query", "Use a valid operation and a date from 1970 through today.");
        return repository.TransactionsAsync(q, status, page, date, kind, clock.UtcNow, ct);
    }
    public Task<AdminKycPage> KycAsync(string? status, int page, CancellationToken ct, string? query = null)
    {
        access.RequireAccess(); ValidatePage(page);
        if (status is not (null or "" or "InProgress" or "PendingReview" or "Verified" or "Rejected" or "NeedsResubmission")) throw new DomainValidationException("admin.invalid_query", "Invalid KYC filter.");
        var search = query?.Trim() ?? "";
        if (search.Length > 254 || search.Any(char.IsControl)) throw new DomainValidationException("admin.invalid_query", "Search must contain at most 254 characters.");
        return repository.KycAsync(status, page, search, clock.UtcNow, ct);
    }
    private static void ValidatePage(int page) { if (page is < 1 or > 100000) throw new DomainValidationException("pagination.invalid", "Page must be between 1 and 100000."); }
    private async Task ConfirmAsync(string password, CancellationToken ct)
    {
        access.RequireAccess();
        if (string.IsNullOrEmpty(password) || password.Length > 128) throw new DomainValidationException("auth.password_required", "Confirm your password.");
        AuthSession confirmation;
        try { confirmation = await identity.LoginAsync(user.Profile.Email.Value, password, ct); }
        catch (ApplicationFailureException e) when (e.Kind == FailureKind.Unauthorized) { throw new ApplicationFailureException(FailureKind.Forbidden, "admin.confirmation_failed", "Password confirmation failed."); }
        await identity.LogoutAsync(confirmation.AccessToken, ct);
        if (confirmation.User.Id != user.Profile.Id || !confirmation.User.IsAdmin || !confirmation.User.EmailConfirmed)
            throw new ApplicationFailureException(FailureKind.Forbidden, "admin.confirmation_failed", "Administrator confirmation failed.");
    }
    private async Task<bool> ExistingAsync(Guid key, string fingerprint, CancellationToken ct)
    {
        var existing = await repository.AuditByKeyAsync(user.Profile.Id, key, ct);
        if (existing is null) return false;
        if (existing.Fingerprint != fingerprint) throw new ApplicationFailureException(FailureKind.Conflict, "transaction.key_reused", "The request key was used for another action.");
        return true;
    }
    public async Task<AdminClientDetail> ControlsAsync(Guid owner, bool blocked, bool frozen, string reason, string password, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key); await ConfirmAsync(password, ct);
        var fingerprint = FinanceOperation.Fingerprint($"Control|{owner:N}|{blocked}|{frozen}|{reason.Trim()}");
        var detail = await repository.ClientAsync(owner, ct);
        return await work.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            if (await ExistingAsync(key, fingerprint, token)) return await repository.ClientAsync(owner, token);
            if (owner == user.Profile.Id) throw new DomainValidationException("admin.self_change", "You cannot restrict your own account.");
            var audit = new AdminAudit(user.Profile.Id, owner, key, "ClientControls", reason, fingerprint, clock.UtcNow);
            // Serialize account creation and controls for the same client.
            var control = await repository.ControlAsync(owner, true, token);
            if (control is null) { control = new ClientControl(owner); repository.Add(control); }
            control.Update(blocked, frozen, clock.UtcNow);
            var cards = await repository.ClientCardsAsync(owner, token);
            var accounts = await repository.ClientAccountsAsync(owner, token);
            foreach (var account in await bank.LockAccountsAsync(accounts.Select(a => a.Id).ToArray(), token)) account.SetBlocked(blocked);
            foreach (var card in cards) card.SetAdminFrozen(frozen);
            repository.Add(audit);
            return new AdminClientDetail(detail.Client, detail.Phone, blocked, frozen,
                accounts.Select(a => new AdminAccount(a.Id, a.CurrencyCode, a.BalanceMinorUnits / 100m, a.Iban, blocked)).ToArray(), [Map(audit)]);
        }, ct);
    }
    public async Task<TransactionResponse> AdjustAsync(Guid owner, Guid accountId, decimal value, bool credit, string reason, string password, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key); var amount = Money.FromDecimal(value); amount.RequirePositive(); await ConfirmAsync(password, ct);
        var fingerprint = FinanceOperation.Fingerprint($"Adjustment|{owner:N}|{accountId:N}|{amount.MinorUnits}|{credit}|{reason.Trim()}");
        return await work.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            if (await ExistingAsync(key, fingerprint, token))
            {
                var existing = await bank.ByKeyAsync(user.Profile.Id, key, token) ?? throw new ApplicationFailureException(FailureKind.Conflict, "admin.inconsistent_audit", "The adjustment needs review.");
                return TransactionService.ToResponse(existing);
            }
            var account = await bank.OwnedAccountAsync(owner, accountId, token) ?? throw new ApplicationFailureException(FailureKind.NotFound, "account.not_found", "Account not found.");
            var clearing = SystemAccounts.Clearing(account.CurrencyCode);
            var locked = (await bank.LockAccountsAsync([accountId, clearing], token)).ToDictionary(a => a.Id);
            var transaction = new BankTransaction(user.Profile.Id, key, fingerprint, TransactionKind.AdminAdjustment,
                credit ? clearing : accountId, credit ? accountId : clearing, null, amount, Money.FromMinorUnits(0), clock.UtcNow, account.CurrencyCode);
            var audit = new AdminAudit(user.Profile.Id, owner, key, "BalanceAdjustment", reason, fingerprint, clock.UtcNow, transaction.Id);
            bank.Add(transaction);
            bank.AddEntries(PostingPlan.Apply(transaction, locked, [(accountId, credit ? amount.MinorUnits : -amount.MinorUnits), (clearing, credit ? -amount.MinorUnits : amount.MinorUnits)], clock.UtcNow));
            repository.Add(audit); return TransactionService.ToResponse(transaction);
        }, ct);
    }
    public async Task<bool> ReviewAsync(Guid verificationId, bool approved, string reason, string password, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key); await ConfirmAsync(password, ct);
        var fingerprint = FinanceOperation.Fingerprint($"ManualKyc|{verificationId:N}|{approved}|{reason.Trim()}");
        return await work.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            if (await ExistingAsync(key, fingerprint, token)) return true;
            var verification = await repository.VerificationAsync(verificationId, token) ?? throw new ApplicationFailureException(FailureKind.NotFound, "kyc.not_found", "Verification not found.");
            if (verification.OwnerId == user.Profile.Id) throw new DomainValidationException("admin.self_review", "You cannot review your own identity.");
            if (!await repository.IsLatestVerificationAsync(verification, token)) throw new ApplicationFailureException(FailureKind.Conflict, "kyc.stale_attempt", "A newer verification attempt exists.");
            if (verification.ProviderSessionId is null) throw new DomainValidationException("kyc.not_submitted", "The client has not submitted a verification session.");
            if (approved)
            {
                var session = await BoundSessionAsync(verification, token);
                if (session.Identity is null || !(session.MandatoryChecksMet || session.Status == KycStatus.Verified && session.RequirementsMet) || session.Evidence?.FrontImage is null)
                    throw new DomainValidationException("kyc.missing_identity", "The document evidence and mandatory identity/biometric checks must be available and approved. Request a new submission.");
                var protectedData = identityVault.Seal(verification.Id, session.Identity);
                if ((await identityRepository.LatestIdentityAsync(verification.Id, token))?.Digest != protectedData.Digest)
                    identityRepository.Add(new KycIdentitySnapshot(verification.Id, protectedData.Ciphertext, protectedData.Digest, clock.UtcNow, protectedData.TaxFingerprint));
            }
            var audit = new AdminAudit(user.Profile.Id, verification.OwnerId, key, approved ? "KycApproved" : "KycRejected", reason, fingerprint, clock.UtcNow);
            repository.Add(audit); repository.Add(new ManualKycReview(verification.Id, user.Profile.Id, audit.Id, approved, clock.UtcNow)); return true;
        }, ct);
    }
    public async Task<bool> TransactionActionAsync(Guid id, bool compensate, string reason, string password, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key); await ConfirmAsync(password, ct);
        var fingerprint = FinanceOperation.Fingerprint($"TransactionAction|{id:N}|{compensate}|{reason.Trim()}");
        return await work.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            if (await ExistingAsync(key, fingerprint, token)) return true;
            var original = await repository.TransactionAsync(id, token) ?? throw new ApplicationFailureException(FailureKind.NotFound, "transaction.not_found", "Transaction not found.");
            var action = compensate ? "TransactionCompensation" : "TransactionFlagged";
            BankTransaction? reversal = null;
            if (compensate)
            {
                if (original.Status != TransactionStatus.Completed || original.Kind == TransactionKind.Compensation || await repository.IsCompensatedAsync(id, token))
                    throw new ApplicationFailureException(FailureKind.Conflict, "transaction.not_compensable", "This transaction cannot be compensated.");
                var entries = await repository.EntriesAsync(id, token);
                var accounts = (await bank.LockAccountsAsync(entries.Select(e => e.AccountId).ToArray(), token)).ToDictionary(a => a.Id);
                var exchange = original.Kind == TransactionKind.Exchange;
                var principal = exchange ? original.ReceivedMinorUnits ?? throw new DomainValidationException("exchange.invalid", "The original exchange metadata is incomplete.") : original.AmountMinorUnits;
                var currency = exchange ? original.DestinationCurrency! : original.CurrencyCode;
                reversal = new BankTransaction(user.Profile.Id, key, fingerprint, TransactionKind.Compensation,
                    original.DestinationAccountId ?? original.SourceAccountId, original.SourceAccountId, null,
                    Money.FromMinorUnits(principal), Money.FromMinorUnits(0), clock.UtcNow, currency);
                if (exchange) reversal.DescribeExchange(original.CurrencyCode, Money.FromMinorUnits(original.AmountMinorUnits),
                    1m / original.ExchangeRate!.Value, original.RateDate!.Value);
                reversal.DescribeMemo($"Compensation for {original.Id:D}");
                bank.Add(reversal); bank.AddEntries(PostingPlan.Apply(reversal, accounts, entries.Select(e => (e.AccountId, -e.AmountMinorUnits)), clock.UtcNow));
            }
            repository.Add(new AdminAudit(user.Profile.Id, original.InitiatorId, key, action, reason, fingerprint, clock.UtcNow, reversal?.Id ?? original.Id, original.Id));
            return true;
        }, ct);
    }
    public async Task<bool> NotifyAsync(Guid? owner, string title, string body, string reason, string password, Guid key, CancellationToken ct)
    {
        FinanceOperation.RequireKey(key);
        _ = new Notification(user.Profile.Id, title, body, clock.UtcNow);
        await ConfirmAsync(password, ct);
        var fingerprint = FinanceOperation.Fingerprint($"Notification|{owner:N}|{title}|{body}|{reason.Trim()}");
        return await work.ExecuteAsync(user.Profile.Id, key, async token =>
        {
            if (await ExistingAsync(key, fingerprint, token)) return true;
            var recipients = await repository.NotificationRecipientsAsync(owner, token);
            var audit = new AdminAudit(user.Profile.Id, owner ?? user.Profile.Id, key, "NotificationSent", reason, fingerprint, clock.UtcNow);
            foreach (var recipient in recipients) repository.Add(new Notification(recipient, title, body, clock.UtcNow));
            repository.Add(audit); return true;
        }, ct);
    }
    private static AdminAuditResponse Map(AdminAudit a) => new(a.Id, a.ActorId, a.Action, a.Reason, a.TransactionId, a.CreatedAt);
}
