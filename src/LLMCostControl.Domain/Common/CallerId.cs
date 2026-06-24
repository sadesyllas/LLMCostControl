namespace LLMCostControl.Domain.Common;

/// <summary>
/// Identifies the end user whose budget is being tracked. Typically an email
/// address, supplied by the gateway on each check/capture call.
/// </summary>
/// <param name="Value">The raw caller id string (e.g. an email address).</param>
public readonly record struct CallerId(string Value)
{
    /// <summary>
    /// Creates a <see cref="CallerId"/> from an email string, trimming whitespace.
    /// </summary>
    /// <param name="email">The email or identifier; cannot be empty.</param>
    /// <returns>A normalised <see cref="CallerId"/>.</returns>
    public static CallerId From(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Caller id cannot be empty.", nameof(email));
        }

        return new CallerId(email.Trim());
    }

    /// <summary>Returns the caller id value.</summary>
    public override string ToString() => Value;
}
