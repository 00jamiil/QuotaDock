namespace QuotaDock.Connectors.OpenAI;

/// <summary>
/// A curated catalog of well-known OpenAI-compatible providers. These are just
/// convenience defaults for the "Add provider" dialog: they prefill the base URL
/// (and a common default model) so the user does not have to remember them. The
/// user still supplies their own key, and QuotaDock validates the model through
/// the provider's own <c>/v1/models</c> endpoint before saving anything.
/// </summary>
public sealed record OpenAiCompatiblePreset(
    string Id,
    string DisplayName,
    string BaseUrl,
    string DefaultModel,
    bool RequiresKey = true);

public static class OpenAiCompatiblePresets
{
    public static IReadOnlyList<OpenAiCompatiblePreset> All { get; } =
    [
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-4o-mini"),
        new("deepseek", "DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat"),
        new("groq", "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile"),
        new("mistral", "Mistral", "https://api.mistral.ai/v1", "mistral-large-latest"),
        new("together", "Together AI", "https://api.together.xyz/v1", "meta-llama/Llama-3.3-70B-Instruct-Turbo"),
        new("fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1", "accounts/fireworks/models/llama-v3p3-70b-instruct"),
        new("xai", "xAI (Grok)", "https://api.x.ai/v1", "grok-2-latest"),
        new("perplexity", "Perplexity", "https://api.perplexity.ai", "sonar"),
        new("moonshot", "Moonshot (Kimi)", "https://api.moonshot.ai/v1", "kimi-k2-0711-preview"),
        new("alibaba-intl", "Alibaba Model Studio (Intl)", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1", "qwen-plus"),
        new("openai", "OpenAI (direct)", "https://api.openai.com/v1", "gpt-4o-mini"),
        new("ollama", "Ollama (local)", "http://localhost:11434/v1", "llama3.2", RequiresKey: false),
        new("lmstudio", "LM Studio (local)", "http://localhost:1234/v1", "local-model", RequiresKey: false),
    ];

    public static OpenAiCompatiblePreset? FindById(string id) =>
        All.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));
}
