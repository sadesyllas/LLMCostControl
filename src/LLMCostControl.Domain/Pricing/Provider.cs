namespace LLMCostControl.Domain.Pricing;

/// <summary>
/// Identifies an LLM provider. Used to select the appropriate pricing adapter.
/// </summary>
public enum Provider
{
    /// <summary>OpenAI (e.g. gpt-4o).</summary>
    OpenAI,

    /// <summary>Anthropic (e.g. claude-3-5-sonnet).</summary>
    Anthropic,

    /// <summary>Google (e.g. gemini-1.5-pro).</summary>
    Google,
}
