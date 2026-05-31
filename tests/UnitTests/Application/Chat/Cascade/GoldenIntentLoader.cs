using System.Text.Json;
using FinFlow.Application.Chat.Cascade;
using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.UnitTests.Application.Chat.Cascade;

internal sealed class GoldenIntentEntry
{
    public string Id { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public required ExpectedClassification Expected { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    internal sealed class ExpectedClassification
    {
        public ChatExecutionMode Mode { get; init; }
        public ChatIntentFamily Family { get; init; }
        public ChatReportingTask Task { get; init; }
    }
}

internal static class GoldenIntentLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static IReadOnlyList<GoldenIntentEntry> LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        var list = new List<GoldenIntentEntry>(items.GetArrayLength());
        foreach (var element in items.EnumerateArray())
        {
            list.Add(JsonSerializer.Deserialize<GoldenIntentEntry>(element.GetRawText(), Options)!);
        }
        return list;
    }

    public static string ResolveDefaultPath()
    {
        // Tests run from bin/Debug/netX.0/. Walk up to repo root then into tests dir.
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(current, "tests", "UnitTests", "Application", "Chat", "Cascade", "golden-set.json");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }
        // Fallback: cùng thư mục với assembly (CopyToOutputDirectory pattern).
        var coLocated = Path.Combine(AppContext.BaseDirectory, "Application", "Chat", "Cascade", "golden-set.json");
        return File.Exists(coLocated) ? coLocated : throw new FileNotFoundException("golden-set.json not found.");
    }
}
