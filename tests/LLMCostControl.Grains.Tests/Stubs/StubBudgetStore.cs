using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Grains.Storage;

namespace LLMCostControl.Grains.Tests;

/// <summary>
/// Stub <see cref="IBudgetStore"/> that returns preset <see cref="EffectiveBudget"/>
/// values per caller id and records call counts.
/// </summary>
public sealed class StubBudgetStore : IBudgetStore
{
    private readonly Dictionary<(string CallerId, BudgetPeriodType PeriodType), EffectiveBudget> _budgets = new();
    private int _callCount;
    private readonly List<BudgetPeriodType> _periodsCalled = new();

    /// <summary>Number of times <see cref="ResolveAsync"/> was called.</summary>
    public int CallCount => _callCount;

    /// <summary>The sequence of budget period types passed to each call.</summary>
    public IReadOnlyList<BudgetPeriodType> PeriodsCalled => _periodsCalled;

    /// <summary>Sets the effective budget that will be returned for a caller id.</summary>
    public void SetBudget(string callerId, EffectiveBudget budget) => SetBudget(callerId, budget.PeriodType, budget);

    /// <summary>Sets the effective budget that will be returned for a caller id and period type.</summary>
    public void SetBudget(string callerId, BudgetPeriodType periodType, EffectiveBudget budget) => _budgets[(callerId, periodType)] = budget;

    /// <summary>Removes the budget for a caller (simulates no budget / unbudgeted).</summary>
    public void RemoveBudget(string callerId, BudgetPeriodType periodType = BudgetPeriodType.Monthly) => _budgets.Remove((callerId, periodType));

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
    public Task<EffectiveBudget> ResolveAsync(CallerId callerId, BudgetPeriodType periodType, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        lock (_periodsCalled)
        {
            _periodsCalled.Add(periodType);
        }

        _budgets.TryGetValue((callerId.Value, periodType), out var budget);
        return Task.FromResult(budget ?? EffectiveBudget.None(periodType));
    }
}
