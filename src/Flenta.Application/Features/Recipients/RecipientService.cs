using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Application.Abstractions.Desktop;
using Flenta.Application.Features.Accounts;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
namespace Flenta.Application.Features.Recipients;

public sealed record RecipientResponse(Guid Id, string Name, string Iban);
public sealed class RecipientService(IRecipientRepository repository, IBankingUnitOfWork work, ICurrentUser user, IClock clock)
{
    public async Task<IReadOnlyList<RecipientResponse>> ListAsync(CancellationToken ct) => (await repository.RecipientsAsync(user.Profile.Id, ct)).Select(Map).ToArray();
    public Task<RecipientResponse> AddAsync(string name, string iban, CancellationToken ct) =>
        work.ExecuteAsync(user.Profile.Id, user.Profile.Id, async token =>
        {
            var list = await repository.RecipientsAsync(user.Profile.Id, token);
            if (list.Count >= 100) throw new DomainValidationException("recipient.limit", "You can save at most 100 recipients.");
            var recipient = new SavedRecipient(user.Profile.Id, name, iban, clock.UtcNow);
            if (list.Any(r => r.Iban == recipient.Iban)) return Map(list.Single(r => r.Iban == recipient.Iban));
            repository.Add(recipient); return Map(recipient);
        }, ct);
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct) =>
        work.ExecuteAsync(user.Profile.Id, user.Profile.Id, async token =>
        {
            var recipient = await repository.RecipientAsync(user.Profile.Id, id, token) ?? throw AccountService.Missing();
            repository.Remove(recipient); return true;
        }, ct);
    private static RecipientResponse Map(SavedRecipient r) => new(r.Id, r.Name, r.Iban);
}
