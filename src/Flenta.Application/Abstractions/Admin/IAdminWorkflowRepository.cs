using Flenta.Application.Features.Admin;
using Flenta.Domain.Entities;
namespace Flenta.Application.Abstractions.Admin;

public interface IAdminWorkflowRepository
{
    Task<IReadOnlyList<Guid>> NotificationRecipientsAsync(Guid? owner, CancellationToken ct);
    void Add(Notification notification);
    Task<BankTransaction?> TransactionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<LedgerEntry>> EntriesAsync(Guid id, CancellationToken ct);
    Task<bool> IsCompensatedAsync(Guid id, CancellationToken ct);
    Task<AdminClientDetail> ClientAsync(Guid owner, CancellationToken ct);
    Task<AdminTransactionPage> TransactionsAsync(string query, string? status, int page, DateOnly? date, string? kind, DateTimeOffset now, CancellationToken ct);
    Task<AdminKycPage> KycAsync(string? status, int page, string query, DateTimeOffset now, CancellationToken ct);
    Task<KycVerification?> VerificationAsync(Guid id, CancellationToken ct);
    Task<bool> IsLatestVerificationAsync(KycVerification verification, CancellationToken ct);
    Task<ClientControl?> ControlAsync(Guid owner, bool forUpdate, CancellationToken ct);
    Task<AdminAudit?> AuditByKeyAsync(Guid actor, Guid key, CancellationToken ct);
    Task<IReadOnlyList<AdminAudit>> AuditAsync(Guid owner, CancellationToken ct);
    Task<IReadOnlyList<BankAccount>> ClientAccountsAsync(Guid owner, CancellationToken ct);
    Task<IReadOnlyList<VirtualCard>> ClientCardsAsync(Guid owner, CancellationToken ct);
    void Add(ClientControl control);
    void Add(AdminAudit audit);
    void Add(ManualKycReview review);
}
