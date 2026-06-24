namespace LLMCostControl.Domain.Common;

public readonly record struct CallerId(string Value)
{
    public static CallerId From(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Caller id cannot be empty.", nameof(email));
        }

        return new CallerId(email.Trim());
    }

    public override string ToString() => Value;
}
