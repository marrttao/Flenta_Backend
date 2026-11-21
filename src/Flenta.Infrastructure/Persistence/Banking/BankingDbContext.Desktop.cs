using Flenta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace Flenta.Infrastructure.Persistence.Banking;

public sealed partial class BankingDbContext
{
    public DbSet<ProfileSettings> ProfileSettings => Set<ProfileSettings>();
    public DbSet<SavedRecipient> SavedRecipients => Set<SavedRecipient>();
    public DbSet<SupportRequest> SupportRequests => Set<SupportRequest>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationReceipt> NotificationReceipts => Set<NotificationReceipt>();
    private static void ConfigureDesktop(ModelBuilder model)
    {
        var settings = model.Entity<ProfileSettings>(); settings.ToTable("profile_settings", t => { t.HasCheckConstraint("ck_settings_currency", "\"DefaultCurrency\" IN ('UAH','USD','EUR')"); t.HasCheckConstraint("ck_settings_avatar", "\"AvatarJpeg\" IS NULL OR octet_length(\"AvatarJpeg\") <= 262144"); });
        settings.HasKey(s => s.Id); settings.Property(s => s.Id).ValueGeneratedNever();
        settings.Property(s => s.Phone).HasMaxLength(16);
        settings.Property(s => s.DefaultCurrency).HasMaxLength(3);
        var recipient = model.Entity<SavedRecipient>(); recipient.ToTable("saved_recipients");
        recipient.HasKey(s => s.Id); recipient.Property(s => s.Id).ValueGeneratedNever();
        recipient.Property(s => s.Name).HasMaxLength(100); recipient.Property(s => s.Iban).HasMaxLength(29);
        recipient.HasIndex(s => new { s.OwnerId, s.Iban }).IsUnique();
        var request = model.Entity<SupportRequest>(); request.ToTable("support_requests", t => t.HasCheckConstraint("ck_support_state", "\"Status\" IN ('Open','Closed') AND \"Kind\" IN ('Message','Callback')"));
        request.HasKey(s => s.Id); request.Property(s => s.Id).ValueGeneratedNever();
        request.Property(s => s.Subject).HasMaxLength(120); request.Property(s => s.Kind).HasMaxLength(16); request.Property(s => s.Status).HasMaxLength(16);
        request.HasIndex(s => new { s.OwnerId, s.Status, s.UpdatedAt }); request.HasIndex(s => new { s.Status, s.UpdatedAt });
        var message = model.Entity<SupportMessage>(); message.ToTable("support_messages");
        message.HasKey(s => s.Id); message.Property(s => s.Id).ValueGeneratedNever();
        message.Property(s => s.Body).HasMaxLength(4000);
        message.HasIndex(s => new { s.RequestId, s.CreatedAt, s.Id });
        message.HasOne<SupportRequest>().WithMany().HasForeignKey(s => s.RequestId).OnDelete(DeleteBehavior.Restrict);
        var note = model.Entity<Notification>(); note.ToTable("notifications");
        note.HasKey(s => s.Id); note.HasAlternateKey(s => new { s.Id, s.OwnerId }); note.Property(s => s.Id).ValueGeneratedNever();
        note.Property(s => s.Title).HasMaxLength(120); note.Property(s => s.Body).HasMaxLength(2000);
        note.HasIndex(s => new { s.OwnerId, s.CreatedAt, s.Id });
        var receipt = model.Entity<NotificationReceipt>(); receipt.ToTable("notification_receipts");
        receipt.HasKey(s => s.Id); receipt.Property(s => s.Id).ValueGeneratedNever();
        receipt.HasIndex(s => new { s.OwnerId, s.NotificationId }).IsUnique();
        receipt.HasOne<Notification>().WithMany().HasForeignKey(s => new { s.NotificationId, s.OwnerId }).HasPrincipalKey(n => new { n.Id, n.OwnerId }).OnDelete(DeleteBehavior.Restrict);
    }
}
