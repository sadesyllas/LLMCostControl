namespace LLMCostControl.Grains.Abstractions;

/// <summary>
/// Thrown by <see cref="IUserBudgetGrain.CaptureUsageAsync"/> when the
/// requested model is not found in the pricing set (§6.2.2: "If the model is
/// unknown / not in the allowed pricing set, capture is rejected with a
/// distinct error").
/// </summary>
[GenerateSerializer]
public sealed class UnknownModelException : Exception
{
    /// <summary>The unknown model name.</summary>
    [Id(0)]
    public string Model { get; set; } = string.Empty;

    /// <summary>Creates the exception with the given model name.</summary>
    public UnknownModelException(string model)
        : base($"Unknown model '{model}': not in the allowed pricing set.")
    {
        Model = model;
    }

    /// <summary>Parameterless constructor for serialization.</summary>
    public UnknownModelException() { }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public UnknownModelException(string message, Exception innerException)
        : base(message, innerException) { }
}
