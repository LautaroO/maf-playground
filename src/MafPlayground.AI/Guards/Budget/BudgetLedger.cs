using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Guards.Budget;

public sealed class BudgetLedger(BudgetGuardOptions options)
{
    private readonly object _gate = new();
    private readonly BudgetGuardOptions _options = options;
    private int _modelCalls;
    private int _toolCalls;
    private long _inputTokens;
    private long _outputTokens;
    private long _reservedInputTokens;
    private long _reservedOutputTokens;
    private decimal _cost;
    private decimal _reservedCost;

    public BudgetReservation ReserveModelCall(
        long estimatedInputTokens,
        int maximumOutputTokens,
        ModelTokenPrice? price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedInputTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputTokens);

        lock (_gate)
        {
            EnsureAvailable("model_calls", _modelCalls + 1, _options.MaxModelCalls);
            EnsureAvailable(
                "input_tokens",
                _inputTokens + _reservedInputTokens + estimatedInputTokens,
                _options.MaxInputTokens);
            EnsureAvailable(
                "output_tokens",
                _outputTokens + _reservedOutputTokens + maximumOutputTokens,
                _options.MaxOutputTokens);

            decimal estimatedCost = CalculateEstimatedCost(
                estimatedInputTokens,
                maximumOutputTokens,
                price);
            if (_options.MaxCostPerRun is decimal maximumCost)
            {
                EnsureAvailable(
                    "cost",
                    _cost + _reservedCost + estimatedCost,
                    maximumCost);
            }

            _modelCalls++;
            _reservedInputTokens += estimatedInputTokens;
            _reservedOutputTokens += maximumOutputTokens;
            _reservedCost += estimatedCost;
            GuardTelemetry.RecordBudgetDecision("reserved", "model_call");
            return new BudgetReservation(
                this,
                estimatedInputTokens,
                maximumOutputTokens,
                estimatedCost,
                price);
        }
    }

    public void ConsumeToolCall()
    {
        lock (_gate)
        {
            EnsureAvailable("tool_calls", _toolCalls + 1, _options.MaxToolCalls);
            _toolCalls++;
            GuardTelemetry.RecordBudgetDecision("consumed", "tool_call");
        }
    }

    internal void Complete(BudgetReservation reservation, UsageDetails? usage)
    {
        lock (_gate)
        {
            _reservedInputTokens -= reservation.ReservedInputTokens;
            _reservedOutputTokens -= reservation.ReservedOutputTokens;
            _reservedCost -= reservation.ReservedCost;

            if (usage?.InputTokenCount is long actualInput &&
                usage.OutputTokenCount is long actualOutput)
            {
                _inputTokens += actualInput;
                _outputTokens += actualOutput;
                _cost += CalculateCost(actualInput, actualOutput, reservation.Price);
            }
            else if (_options.Enforcement == BudgetEnforcement.Hard)
            {
                _inputTokens += reservation.ReservedInputTokens;
                _outputTokens += reservation.ReservedOutputTokens;
                _cost += reservation.ReservedCost;
            }

            GuardTelemetry.RecordBudgetDecision("reconciled", "model_call");
        }
    }

    internal void Cancel(BudgetReservation reservation)
    {
        lock (_gate)
        {
            _reservedInputTokens -= reservation.ReservedInputTokens;
            _reservedOutputTokens -= reservation.ReservedOutputTokens;
            _reservedCost -= reservation.ReservedCost;
            GuardTelemetry.RecordBudgetDecision("released", "model_call");
        }
    }

    private decimal CalculateEstimatedCost(
        long inputTokens,
        long outputTokens,
        ModelTokenPrice? price)
    {
        if (_options.MaxCostPerRun is null)
        {
            return 0;
        }

        if (price is null)
        {
            if (_options.Enforcement == BudgetEnforcement.Hard)
            {
                throw new GuardConfigurationException(
                    "A hard monetary budget requires pricing for the selected provider and model.");
            }

            return 0;
        }

        if (!string.Equals(price.Currency, _options.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new GuardConfigurationException(
                $"Budget currency '{_options.Currency}' does not match model pricing currency '{price.Currency}'.");
        }

        return CalculateCost(inputTokens, outputTokens, price);
    }

    private static decimal CalculateCost(
        long inputTokens,
        long outputTokens,
        ModelTokenPrice? price) => price is null
            ? 0
            : ((decimal)inputTokens * price.InputPerMillionTokens +
               (decimal)outputTokens * price.OutputPerMillionTokens) / 1_000_000m;

    private static void EnsureAvailable(string resource, long requested, long maximum)
    {
        if (requested > maximum)
        {
            GuardTelemetry.RecordBudgetDecision("blocked", resource);
            throw new BudgetExceededException(resource, requested, maximum);
        }
    }

    private static void EnsureAvailable(string resource, decimal requested, decimal maximum)
    {
        if (requested > maximum)
        {
            GuardTelemetry.RecordBudgetDecision("blocked", resource);
            throw new BudgetExceededException(resource, requested, maximum);
        }
    }
}

public sealed class BudgetReservation : IDisposable
{
    private readonly BudgetLedger _ledger;
    private int _completed;

    internal BudgetReservation(
        BudgetLedger ledger,
        long reservedInputTokens,
        int reservedOutputTokens,
        decimal reservedCost,
        ModelTokenPrice? price)
    {
        _ledger = ledger;
        ReservedInputTokens = reservedInputTokens;
        ReservedOutputTokens = reservedOutputTokens;
        ReservedCost = reservedCost;
        Price = price;
    }

    internal long ReservedInputTokens { get; }

    internal int ReservedOutputTokens { get; }

    internal decimal ReservedCost { get; }

    internal ModelTokenPrice? Price { get; }

    public void Complete(UsageDetails? usage)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _ledger.Complete(this, usage);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _ledger.Cancel(this);
        }
    }
}

public sealed class BudgetExceededException(
    string resource,
    object requested,
    object maximum)
    : InvalidOperationException(
        $"The AI execution budget for '{resource}' was exceeded. Requested {requested}; maximum {maximum}.")
{
    public string Resource { get; } = resource;
}

