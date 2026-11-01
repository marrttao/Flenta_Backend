using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flenta.Infrastructure.Persistence.Banking;

public sealed partial class BankingDbContext
{
    public DbSet<KycVerification> KycVerifications => Set<KycVerification>();
    public DbSet<KycReviewEvent> KycReviewEvents => Set<KycReviewEvent>();
    public DbSet<KycWebhookReceipt> KycWebhookReceipts => Set<KycWebhookReceipt>();

    private static void ConfigureKyc(ModelBuilder model)
    {
        var verification = model.Entity<KycVerification>();
        verification.ToTable("verifications", "kyc", table =>
        {
            table.HasCheckConstraint("ck_kyc_status", "\"Status\" IN ('NotStarted','InProgress','PendingReview','NeedsResubmission','Rejected','Verified','Expired')");
            table.HasCheckConstraint("ck_kyc_verified", "(\"Status\" = 'Verified') = (\"VerifiedAt\" IS NOT NULL)");
        });
        verification.HasKey(v => v.Id);
        verification.Property(v => v.Id).ValueGeneratedNever();
        verification.HasAlternateKey(v => new { v.Id, v.OwnerId });
        verification.HasIndex(v => new { v.OwnerId, v.ProviderName, v.AttemptNumber }).IsUnique();
        verification.Property(v => v.ProviderName).HasMaxLength(32).HasDefaultValue("Sumsub");
        verification.Property(v => v.AttemptNumber).HasDefaultValue(1);
        verification.HasIndex(v => v.ExternalUserId);
        verification.HasIndex(v => new { v.ProviderName, v.ProviderSessionId }).IsUnique();
        verification.Property(v => v.ExternalUserId).HasMaxLength(64);
        verification.Property(v => v.ConfigurationId).HasMaxLength(100);
        verification.Property(v => v.ProviderSessionId).HasMaxLength(100);
        verification.Property(v => v.DecisionReference).HasMaxLength(100);
        verification.Property(v => v.FailureCode).HasMaxLength(100);
        verification.Property(v => v.Status).HasConversion<string>().HasMaxLength(32);

        var review = model.Entity<KycReviewEvent>();
        review.ToTable("review_events", "kyc");
        review.HasKey(e => e.Id);
        review.Property(e => e.Id).ValueGeneratedNever();
        review.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        review.Property(e => e.PreviousStatus).HasConversion<string>().HasMaxLength(32);
        review.Property(e => e.FailureCode).HasMaxLength(100);
        review.HasIndex(e => new { e.OwnerId, e.CreatedAt });
        review.HasOne<KycVerification>().WithMany().HasForeignKey(e => new { e.VerificationId, e.OwnerId })
            .HasPrincipalKey(v => new { v.Id, v.OwnerId }).OnDelete(DeleteBehavior.Restrict);

        var receipt = model.Entity<KycWebhookReceipt>();
        receipt.ToTable("webhook_receipts", "kyc");
        receipt.HasKey(r => r.Digest);
        receipt.Property(r => r.Digest).HasMaxLength(64);
        receipt.HasIndex(r => new { r.OwnerId, r.ReceivedAt });
    }
}
