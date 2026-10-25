using System.Globalization;
using System.Security.Cryptography;
using Flenta.Application.Features.Auth;
using Flenta.Application.Features.Transactions;
using Flenta.Domain.Entities;
using Flenta.Domain.Exceptions;
using Flenta.Domain.ValueObjects;
using Flenta.Infrastructure.Persistence.Banking;
using Flenta.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Flenta.Tests.Banking;

public sealed class DomainTests
{
    [Theory]
    [InlineData("0.01", 1)]
    [InlineData("12.34", 1234)]
    [InlineData("0", 0)]
    public void Money_uses_exact_minor_units(string value, long expected) =>
        Assert.Equal(expected, Money.FromDecimal(decimal.Parse(value, CultureInfo.InvariantCulture)).MinorUnits);

    [Theory]
    [InlineData("0.001")]
    [InlineData("-1")]
    [InlineData("10000000000.01")]
    public void Invalid_money_is_rejected(string value) =>
        Assert.Throws<DomainValidationException>(() => Money.FromDecimal(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Fact]
    public void Generated_sandbox_iban_has_a_valid_checksum()
    {
        var iban = Iban.CreateSandbox(Guid.NewGuid());
        Assert.Equal(29, iban.Value.Length);
        Assert.StartsWith("UA", iban.Value);
        Assert.Equal(iban, Iban.Parse(iban.Value.ToLowerInvariant()));
        var bad = iban.Value[..^1] + (iban.Value[^1] == '0' ? '1' : '0');
        Assert.Throws<DomainValidationException>(() => Iban.Parse(bad));
    }

    [Fact]
    public void Account_cannot_be_overdrawn_and_failed_debit_keeps_its_balance()
    {
        var account = BankAccount.Open(Guid.NewGuid(), DateTimeOffset.UtcNow);
        account.Credit(Money.FromDecimal(10));
        Assert.Throws<DomainValidationException>(() => account.Debit(Money.FromDecimal(10.01m)));
        Assert.Equal(1000, account.BalanceMinorUnits);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("500", "2.50")]
    [InlineData("501", "2.51")]
    public void Iban_fee_applies_minimum_and_rounds_to_kopecks(string amount, string expected)
    {
        var policy = new SandboxFeePolicy(.005m, Money.FromDecimal(1));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), policy.Calculate(TransactionKind.IbanTransfer,
            Money.FromDecimal(decimal.Parse(amount, CultureInfo.InvariantCulture))).Amount);
        Assert.Equal(0, policy.Calculate(TransactionKind.InternalTransfer, Money.FromDecimal(500)).MinorUnits);
    }

    [Fact]
    public void Completed_transactions_require_balanced_entries_and_cannot_be_rewritten()
    {
        var now = DateTimeOffset.UtcNow;
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var operation = new BankTransaction(Guid.NewGuid(), Guid.NewGuid(), "fingerprint", TransactionKind.InternalTransfer,
            source, target, null, Money.FromDecimal(10), Money.FromDecimal(0), now);
        Assert.Throws<DomainValidationException>(() => operation.Complete([new(operation.Id, source, -1000, now), new(operation.Id, target, 900, now)], now));
        Assert.Equal(TransactionStatus.Pending, operation.Status);
        operation.Complete([new(operation.Id, source, -1000, now), new(operation.Id, target, 1000, now)], now);
        Assert.Throws<DomainValidationException>(() => operation.Reject("anything", now));
    }

    [Fact]
    public void Blocked_closed_and_expired_cards_cannot_reveal_requisites()
    {
        var now = DateTimeOffset.UtcNow;
        var card = new VirtualCard(Guid.NewGuid(), Guid.NewGuid(), "1234", "ciphertext", "fingerprint", now.Month, now.Year + 1, now);
        card.Block();
        Assert.Throws<DomainValidationException>(() => card.MarkSecretsViewed(now));
        card.Unblock();
        card.MarkSecretsViewed(now);
        card.Close();
        Assert.Throws<DomainValidationException>(() => card.Unblock());
        var expired = new VirtualCard(Guid.NewGuid(), Guid.NewGuid(), "1234", "ciphertext", "fingerprint", 1, now.Year - 1, now);
        Assert.Throws<DomainValidationException>(() => expired.MarkSecretsViewed(now));
    }

    [Fact]
    public void Card_ciphertext_is_bound_to_owner_and_card_and_detects_tampering()
    {
        var vault = new SandboxCardVault(Options.Create(new BankingOptions
        {
            Enabled = true, CardEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));
        var owner = Guid.NewGuid();
        var card = Guid.NewGuid();
        var data = vault.Create(owner, card);
        var secrets = vault.Reveal(owner, card, data.Ciphertext);
        Assert.StartsWith("999999", secrets.Number);
        Assert.Equal(16, secrets.Number.Length);
        Assert.Matches("^[0-9]{3}$", secrets.Cvv);
        Assert.EndsWith(data.Last4, secrets.Number);
        Assert.DoesNotContain(secrets.Number, data.Ciphertext);
        Assert.Throws<ApplicationFailureException>(() => vault.Reveal(Guid.NewGuid(), card, data.Ciphertext));
        Assert.Throws<ApplicationFailureException>(() => vault.Reveal(owner, Guid.NewGuid(), data.Ciphertext));
        var corrupted = Convert.FromBase64String(data.Ciphertext[3..]);
        corrupted[^1] ^= 1;
        Assert.Throws<ApplicationFailureException>(() => vault.Reveal(owner, card, "v1." + Convert.ToBase64String(corrupted)));
    }
}
