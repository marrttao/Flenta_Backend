using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Admin;
using Flenta.Application.Features.Auth;
using Flenta.Domain.Exceptions;

namespace Flenta.Application.Features.Admin;

public sealed record AdminTransaction(Guid Id, Guid ClientId, string ClientName, string? ClientEmail,
    string Kind, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt);
public sealed record AdminOverviewResponse(int TotalClients, int VerifiedKycClients, int TodayTransactions,
    DateOnly Date, DateTimeOffset UpdatedAt, IReadOnlyList<AdminTransaction> RecentTransactions);
public sealed record AdminClient(Guid Id, string DisplayName, string? Email, bool EmailConfirmed, string KycStatus, DateTimeOffset CreatedAt);
public sealed record AdminClientsResponse(IReadOnlyList<AdminClient> Items, int Total, int TotalClients, int Page, int PageSize);

public sealed class AdminService(IAdminRepository repository, ICurrentUser user, IClock clock)
{
    public void RequireAccess()
    {
        if (!user.Profile.IsAdmin || !user.Profile.EmailConfirmed)
            throw new ApplicationFailureException(FailureKind.Forbidden, "admin.forbidden", "Administrator access is required.");
    }

    public Task<AdminOverviewResponse> OverviewAsync(CancellationToken ct)
    {
        RequireAccess();
        return repository.OverviewAsync(clock.UtcNow, ct);
    }

    public Task<AdminClientsResponse> ClientsAsync(string? email, int page, int pageSize, CancellationToken ct)
    {
        RequireAccess();
        var query = email?.Trim() ?? "";
        if (query.Length > 254 || query.Any(char.IsControl) || page is < 1 or > 1000000 || pageSize is < 1 or > 100)
            throw new DomainValidationException("admin.invalid_query", "Use a search of at most 254 characters, page 1–1000000 and page size 1–100.");
        return repository.ClientsAsync(query, page, pageSize, ct);
    }
}
