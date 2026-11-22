using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace Flenta.Infrastructure.Persistence.Banking;

public sealed partial class BankingDbContext
{
    public DbSet<AdminAudit> AdminAudit => Set<AdminAudit>();
    public DbSet<ClientControl> ClientControls => Set<ClientControl>();
    public DbSet<ManualKycReview> ManualReviews => Set<ManualKycReview>();
    private static void ConfigureAdminWorkflow(ModelBuilder model)
    {
        var audit = model.Entity<AdminAudit>(); audit.ToTable("admin_audit");
        audit.HasKey(a => a.Id); audit.Property(a => a.Id).ValueGeneratedNever();
        audit.Property(a => a.Action).HasMaxLength(40); audit.Property(a => a.Reason).HasMaxLength(1000); audit.Property(a => a.Fingerprint).HasMaxLength(64);
        audit.HasIndex(a => new { a.ActorId, a.IdempotencyKey }).IsUnique();
        audit.HasIndex(a => new { a.TargetId, a.CreatedAt, a.Id });
        audit.HasIndex(a => a.RelatedTransactionId).IsUnique().HasFilter("\"Action\" = 'TransactionCompensation'");
        audit.HasOne<BankTransaction>().WithMany().HasForeignKey(a => a.RelatedTransactionId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<BankTransaction>().WithMany().HasForeignKey(a => a.TransactionId).OnDelete(DeleteBehavior.Restrict);
        var control = model.Entity<ClientControl>(); control.ToTable("client_controls");
        control.HasKey(c => c.Id); control.Property(c => c.Id).ValueGeneratedNever();
        var review = model.Entity<ManualKycReview>(); review.ToTable("manual_reviews", "kyc");
        review.HasKey(a => a.Id); review.Property(a => a.Id).ValueGeneratedNever();
        review.HasIndex(a => new { a.VerificationId, a.CreatedAt, a.Id });
        review.HasIndex(a => a.AuditId).IsUnique();
        review.HasOne<KycVerification>().WithMany().HasForeignKey(a => a.VerificationId).OnDelete(DeleteBehavior.Restrict);
        review.HasOne<AdminAudit>().WithMany().HasForeignKey(a => a.AuditId).OnDelete(DeleteBehavior.Restrict);
    }
}
