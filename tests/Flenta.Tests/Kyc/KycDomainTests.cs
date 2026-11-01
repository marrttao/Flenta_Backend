using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;

namespace Flenta.Tests.Kyc;

public sealed class KycDomainTests
{
    [Fact]
    public void Verification_cannot_be_approved_without_document_requirements()
    {
        var now = DateTimeOffset.UtcNow; var verification = new KycVerification(Guid.NewGuid(), "level", true, now);
        verification.Start(now);
        verification.ApplyProviderResult("applicant", "review", KycStatus.Verified, false, now);
        Assert.Equal(KycStatus.NeedsResubmission, verification.Status); Assert.Null(verification.VerifiedAt);
        Assert.False(verification.CanAccess("Didit", "level", true));
    }

    [Fact]
    public void Approval_is_bound_to_level_environment_and_applicant_and_can_be_revoked()
    {
        var now = DateTimeOffset.UtcNow; var verification = new KycVerification(Guid.NewGuid(), "level", true, now);
        verification.Start(now); verification.ApplyProviderResult("applicant", "review", KycStatus.Verified, true, now);
        Assert.True(verification.CanAccess("Didit", "level", true));
        Assert.False(verification.CanAccess("Didit", "other-level", true)); Assert.False(verification.CanAccess("Didit", "level", false));
        Assert.Null(verification.ApplyProviderResult("applicant", "review", KycStatus.Verified, true, now));
        Assert.Throws<DomainValidationException>(() => verification.ApplyProviderResult("other-applicant", "review", KycStatus.Verified, true, now));
        Assert.Throws<DomainValidationException>(() => verification.Start(now));
        verification.ApplyProviderResult("applicant", "new-review", KycStatus.PendingReview, false, now);
        Assert.False(verification.CanAccess("Didit", "level", true)); Assert.Null(verification.VerifiedAt);
    }

    [Fact]
    public void Final_rejection_cannot_be_restarted_by_the_user()
    {
        var now = DateTimeOffset.UtcNow; var verification = new KycVerification(Guid.NewGuid(), "level", true, now);
        verification.Start(now); verification.ApplyProviderResult("applicant", "review", KycStatus.Rejected, false, now);
        Assert.Throws<DomainValidationException>(() => verification.Start(now));
        Assert.False(verification.CanAccess("Didit", "level", true));
    }
}
