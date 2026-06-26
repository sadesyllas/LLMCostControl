using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Grains.Storage;

namespace LLMostControl.Grains.Tests;

/// <summary>
/// Stub <see cref="IBudgetStore"/> that returns preset <see cref="EffectiveBudget"/>
/// values per caller id and records call counts. Also tracks the
/// <see cref="BudgetPeriod"/> passed to each call for period-rollover tests.
/// </summary>
public sealed class StubBudgetStore : IBudgetStore
{
    private readonly Dictionary<string, EffectiveBudget> _budgets = new(StringComparer.Ordinal);
    private int _callCount;
    private readonly List<BudgetPeriod> _periodsCalled = new();

    /// <summary>Number of times <see cref="ResolveAsync"/> was called.</summary>
    public int CallCount => _callCount;

    /// <summary>The sequence of budget periods passed to each call.</summary>
    public IReadOnlyList<BudgetPeriod> PeriodsCalled => _periodsCalled;

    /// <summary>Sets the effective budget that will be returned for a caller id.</summary>
    public void SetBudget(string callerId, EffectiveBudget budget) => _budgets[callerId] = budget;

    /// <summary>Removes the budget for a caller (simulates no budget / unbudgeted).</summary>
    public void RemoveBudget(string callerId) => _budgets.Remove(callerId);

    /// <summary>Resets the store to empty.</summary>
    public void Reset()
    {
        _budgets.Clear();
        _callCount = 0;
        lock (_periodsCalled)
        {
            _periodsCalled.Clear();
        }
    }

    /// <summary>Returns the preset budget for the caller, or <see cref="EffectiveBudget.None"/> if unset.</summary>
    public Task<EffectiveBudget> ResolveAsync(CallerId callerId, BudgetPeriod period, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        lock (_periodsCalled)
        {
            _periodsCalled.Add(period);
        }

        _budgets.TryGetValue(callerId.Value, out var budget);
        return Task.FromResult(budget ?? EffectiveBudget.None());
    }
}
