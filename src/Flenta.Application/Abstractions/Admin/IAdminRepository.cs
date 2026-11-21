using Flenta.Application.Features.Admin;

namespace Flenta.Application.Abstractions.Admin;

public interface IAdminRepository
{
    Task<AdminOverviewResponse> OverviewAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<AdminClientsResponse> ClientsAsync(string email, int page, int pageSize, CancellationToken cancellationToken);
}
