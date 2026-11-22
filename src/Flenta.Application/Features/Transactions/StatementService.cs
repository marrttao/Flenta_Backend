using Flenta.Application.Abstractions;
using Flenta.Application.Abstractions.Banking;
using Flenta.Domain.Exceptions;
namespace Flenta.Application.Features.Transactions;

public sealed class StatementService(IStatementRepository repository, ICurrentUser user)
{
    public async Task<IReadOnlyList<TransactionResponse>> GetAsync(DateOnly from, DateOnly through, CancellationToken ct)
    {
        if (through == DateOnly.MaxValue || through < from || through.DayNumber - from.DayNumber > 366) throw new DomainValidationException("statement.invalid_range", "Choose up to 367 calendar days.");
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = new DateTimeOffset(through.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddDays(1);
        var rows = await repository.StatementAsync(user.Profile.Id, start, end, ct);
        if (rows.Count > 10000) throw new DomainValidationException("statement.too_large", "Choose a shorter period; the statement exceeds 10000 transactions.");
        return rows.Select(TransactionService.ToResponse).ToArray();
    }
}
