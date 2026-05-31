using System.Reflection;
using System.Text.Json;
using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Cascade;

public sealed class IntentExemplarSeed
{
    public string Text { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public ChatExecutionMode Mode { get; init; }
    public ChatIntentFamily Family { get; init; }
    public ChatReportingTask Task { get; init; }
    public double Weight { get; init; } = 1.0;
}

public static class IntentExemplarSeedLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static IReadOnlyList<IntentExemplarSeed> LoadEmbedded(string resourceName = "intent-exemplars.json")
    {
        var assembly = typeof(IntentExemplarSeedLoader).Assembly;
        var fullName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Embedded resource '{fullName}' could not be opened.");

        return ParseStream(stream);
    }

    public static IReadOnlyList<IntentExemplarSeed> LoadFromStream(Stream stream) => ParseStream(stream);

    public static IReadOnlyList<IntentExemplarSeed> LoadFromText(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return ParseStream(stream);
    }

    private static IReadOnlyList<IntentExemplarSeed> ParseStream(Stream stream)
    {
        var doc = JsonDocument.Parse(stream);
        var items = doc.RootElement.GetProperty("items");
        var list = new List<IntentExemplarSeed>(capacity: items.GetArrayLength());
        foreach (var element in items.EnumerateArray())
        {
            list.Add(new IntentExemplarSeed
            {
                Text = element.GetProperty("text").GetString() ?? string.Empty,
                Language = element.TryGetProperty("language", out var lang) ? lang.GetString() ?? "vi" : "vi",
                Mode = ParseEnum<ChatExecutionMode>(element, "mode"),
                Family = ParseEnum<ChatIntentFamily>(element, "family"),
                Task = ParseEnum<ChatReportingTask>(element, "task"),
                Weight = element.TryGetProperty("weight", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : 1.0
            });
        }
        return list;
    }

    private static T ParseEnum<T>(JsonElement element, string property) where T : struct, Enum
    {
        var value = element.GetProperty(property).GetString();
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
            throw new InvalidOperationException($"Unrecognised {property}: {value}");
        return parsed;
    }
}
