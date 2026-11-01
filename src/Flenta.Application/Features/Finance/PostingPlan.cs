using Flenta.Domain.Entities;
using Flenta.Domain.ValueObjects;

namespace Flenta.Application.Features.Finance;

internal static class PostingPlan
{
    public static IReadOnlyList<LedgerEntry> Apply(BankTransaction operation, IReadOnlyDictionary<Guid, BankAccount> accounts,
        IEnumerable<(Guid Account, long Amount)> changes, DateTimeOffset now)
    {
        var deltas = changes.GroupBy(c => c.Account).Select(g => (Id: g.Key, Amount: g.Sum(c => c.Amount)))
            .Where(c => c.Amount != 0).ToArray();
        // Validate every balance before changing any tracked entity, including destination limits.
        foreach (var delta in deltas)
        {
            var amount = Money.FromMinorUnits(Math.Abs(delta.Amount));
            if (delta.Amount < 0) accounts[delta.Id].EnsureCanDebit(amount);
            else accounts[delta.Id].EnsureCanCredit(amount);
        }
        var entries = deltas.Select(d => new LedgerEntry(operation.Id, d.Id, d.Amount, now, accounts[d.Id].CurrencyCode)).ToArray();
        operation.Complete(entries, now);
        foreach (var delta in deltas)
        {
            var amount = Money.FromMinorUnits(Math.Abs(delta.Amount));
            if (delta.Amount < 0) accounts[delta.Id].Debit(amount);
            else accounts[delta.Id].Credit(amount);
        }
        return entries;
    }
}
