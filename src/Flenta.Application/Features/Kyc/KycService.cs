using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Kyc;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Entities;

namespace Flenta.Application.Features.Kyc;

public sealed record KycStatusResponse(string Provider, string Status, bool CanAccess, bool Sandbox, string? FailureCode, DateTimeOffset? UpdatedAt);
public sealed record KycSessionResponse(string SessionId, string Url, bool Sandbox);

public sealed class KycService(IKycRepository repository, IKycUnitOfWork unitOfWork, IKycProvider provider, ICurrentUser user, IClock clock, IIdentityVault vault)
{
    public async Task<KycStatusResponse> GetAsync(CancellationToken ct)
    {
        RequireEmail();
        var configuration = provider.Configuration;
        var verification = await repository.GetAsync(user.Profile.Id, configuration.Name, false, ct);
        if (verification is not null) RequireConfiguration(verification, configuration);
        return await ResponseAsync(verification, configuration, ct);
    }

    public Task<KycSessionResponse> SessionAsync(CancellationToken ct)
    {
        RequireEmail();
        var configuration = provider.Configuration;
        return unitOfWork.ExecuteAsync(user.Profile.Id, async token =>
        {
            var previous = await repository.GetAsync(user.Profile.Id, configuration.Name, true, token);
            if (previous is not null) RequireConfiguration(previous, configuration);
            var verification = previous;
            if (verification is null || verification.Status == KycStatus.Expired
                || verification.Status == KycStatus.NeedsResubmission && verification.FailureCode == "kyc.requirements_not_met")
            {
                verification = new KycVerification(user.Profile.Id, configuration.ConfigurationId, configuration.Sandbox, clock.UtcNow,
                    configuration.Name, (previous?.AttemptNumber ?? 0) + 1);
                repository.Add(verification);
            }
            var change = verification.Start(clock.UtcNow);
            if (change is not null) repository.Add(change);
            var session = verification.ProviderSessionId is null
                ? await provider.CreateSessionAsync(verification.ExternalUserId, token)
                : await provider.GetSessionAsync(verification.ProviderSessionId, token);
            await ApplyAsync(verification, session, configuration, token);
            if (session.VerificationUrl is null)
                throw new ApplicationFailureException(FailureKind.Conflict, "kyc.session_unavailable", "Verification session cannot be opened. Refresh its status.");
            return new KycSessionResponse(session.SessionId, session.VerificationUrl, configuration.Sandbox);
        }, ct);
    }

    public Task<KycStatusResponse> RefreshAsync(CancellationToken ct)
    {
        RequireEmail();
        var configuration = provider.Configuration;
        return unitOfWork.ExecuteAsync(user.Profile.Id, async token =>
        {
            var verification = await repository.GetAsync(user.Profile.Id, configuration.Name, true, token);
            if (verification is not null)
            {
                RequireConfiguration(verification, configuration);
                if (verification.ProviderSessionId is not null)
                    await ApplyAsync(verification, await provider.GetSessionAsync(verification.ProviderSessionId, token), configuration, token);
            }
            return await ResponseAsync(verification, configuration, token);
        }, ct);
    }

    public async Task ReceiveWebhookAsync(KycNotification notification, CancellationToken ct)
    {
        var configuration = provider.Configuration;
        if (notification.Provider != configuration.Name || notification.ConfigurationId is not null && notification.ConfigurationId != configuration.ConfigurationId
            || notification.Sandbox is not null && notification.Sandbox != configuration.Sandbox) return;
        var known = await repository.FindSessionAsync(configuration.Name, notification.SessionId, ct);
        if (known is null) return;
        await unitOfWork.ExecuteAsync(known.OwnerId, async token =>
        {
            if (await repository.HasReceiptAsync(notification.Digest, token)) return false;
            var verification = await repository.GetAsync(known.OwnerId, configuration.Name, true, token);
            // An event for an old attempt must never approve its replacement.
            if (verification is null || verification.Id != known.Id) return false;
            RequireConfiguration(verification, configuration);
            if (notification.ExternalUserId is not null && notification.ExternalUserId != verification.ExternalUserId) throw Mismatch();
            await ApplyAsync(verification, await provider.GetSessionAsync(notification.SessionId, token), configuration, token);
            repository.Add(new KycWebhookReceipt(notification.Digest, known.OwnerId, clock.UtcNow));
            return true;
        }, ct);
    }

    private async Task ApplyAsync(KycVerification verification, KycSessionSnapshot session, KycProviderConfiguration configuration, CancellationToken ct)
    {
        if (session.ExternalUserId != verification.ExternalUserId || session.ConfigurationId != configuration.ConfigurationId
            || session.Sandbox != configuration.Sandbox) throw Mismatch();
        if (session.Identity is not null)
        {
            var sealedData = vault.Seal(verification.Id, session.Identity);
            if ((await repository.LatestIdentityAsync(verification.Id, ct))?.Digest != sealedData.Digest)
                repository.Add(new KycIdentitySnapshot(verification.Id, sealedData.Ciphertext, sealedData.Digest, clock.UtcNow, sealedData.TaxFingerprint));
        }
        var change = verification.ApplyProviderResult(session.SessionId, session.SessionId, session.Status, session.RequirementsMet, clock.UtcNow);
        if (change is not null) repository.Add(change);
    }

    private void RequireEmail()
    {
        if (!user.Profile.EmailConfirmed)
            throw new ApplicationFailureException(FailureKind.Forbidden, "kyc.email_required", "Confirm your email before identity verification.");
    }
    private static void RequireConfiguration(KycVerification verification, KycProviderConfiguration configuration)
    {
        if (!verification.Matches(configuration.Name, configuration.ConfigurationId, configuration.Sandbox))
            throw new ApplicationFailureException(FailureKind.Conflict, "kyc.configuration_changed", "Verification configuration has changed. Contact support.");
    }
    private static ApplicationFailureException Mismatch() => new(FailureKind.InvalidRequest, "kyc.provider_mismatch", "Verification reference does not match.");
    private async Task<KycStatusResponse> ResponseAsync(KycVerification? verification, KycProviderConfiguration configuration, CancellationToken ct)
    {
        var manual = verification is null ? null : await repository.ManualDecisionAsync(verification, ct);
        return new(configuration.Name, manual.HasValue ? (manual.Value ? "Verified" : "Rejected") : verification?.Status.ToString() ?? "NotStarted",
            manual ?? verification?.CanAccess(configuration.Name, configuration.ConfigurationId, configuration.Sandbox) ?? false,
            configuration.Sandbox, manual == false ? "kyc.manual_rejection" : verification?.FailureCode, verification?.UpdatedAt);
    }
}
