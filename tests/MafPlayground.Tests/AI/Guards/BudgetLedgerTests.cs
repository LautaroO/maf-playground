using MafPlayground.AI;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Budget;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests.AI.Guards;

public sealed class BudgetLedgerTests
{
    [Fact]
    public async Task ParallelReservations_CannotOverspendModelCallLimit()
    {
        BudgetLedger ledger = new(new BudgetGuardOptions
        {
            Enabled = true,
            MaxModelCalls = 2,
            MaxToolCalls = 2,
            MaxInputTokens = 100,
            MaxOutputTokens = 100,
            MaxOutputTokensPerCall = 10,
        });

        Task<bool>[] reservations = Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    using BudgetReservation reservation = ledger.ReserveModelCall(1, 1, null);
                    reservation.Complete(new UsageDetails
                    {
                        InputTokenCount = 1,
                        OutputTokenCount = 1,
                    });
                    return true;
                }
                catch (BudgetExceededException)
                {
                    return false;
                }
            }))
            .ToArray();

        bool[] results = await Task.WhenAll(reservations);

        Assert.Equal(2, results.Count(success => success));
    }

    [Fact]
    public void HardMonetaryBudget_RejectsReservationBeforeCall()
    {
        BudgetLedger ledger = new(new BudgetGuardOptions
        {
            Enabled = true,
            Enforcement = BudgetEnforcement.Hard,
            MaxCostPerRun = 0.01m,
            Currency = "USD",
            MaxModelCalls = 2,
            MaxToolCalls = 2,
            MaxInputTokens = 2_000_000,
            MaxOutputTokens = 2_000_000,
            MaxOutputTokensPerCall = 1_000_000,
        });
        ModelTokenPrice price = new("USD", "test", 0.01m, 0.02m);

        BudgetExceededException exception = Assert.Throws<BudgetExceededException>(() =>
            ledger.ReserveModelCall(1_000_000, 1_000_000, price));

        Assert.Equal("cost", exception.Resource);
    }

    [Fact]
    public void CancelledReservation_ReleasesReservedCapacity()
    {
        BudgetLedger ledger = new(new BudgetGuardOptions
        {
            Enabled = true,
            MaxModelCalls = 2,
            MaxToolCalls = 2,
            MaxInputTokens = 5,
            MaxOutputTokens = 5,
            MaxOutputTokensPerCall = 5,
        });

        ledger.ReserveModelCall(5, 5, null).Dispose();
        using BudgetReservation replacement = ledger.ReserveModelCall(5, 5, null);

        Assert.NotNull(replacement);
    }
}

