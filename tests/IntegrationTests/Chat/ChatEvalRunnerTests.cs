using System.Text.Json;

namespace FinFlow.IntegrationTests.Chat;

/// <summary>
/// Skeleton eval runner. Loads golden dataset and reports basic stats.
///
/// Full implementation (run vs live ChatService, compute keyword coverage + recall)
/// requires seeded test tenant/membership/document chunks. Defer to follow-up spec
/// once seed data fixture is available.
/// </summary>
public sealed class ChatEvalRunnerTests
{
    [Fact]
    public void EvalDataset_LoadsAndParses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Chat", "EvalDataset.json");
        var fallback = "tests/IntegrationTests/Chat/EvalDataset.json";

        var jsonPath = File.Exists(path) ? path : Path.GetFullPath(fallback);
        if (!File.Exists(jsonPath))
        {
            // Eval dataset is optional content; skip when not deployed alongside test binary.
            return;
        }

        var json = File.ReadAllText(jsonPath);
        var entries = JsonSerializer.Deserialize<ChatEvalEntry[]>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(entries);
        Assert.True(entries.Length >= 5, "Eval dataset should contain at least 5 entries.");

        foreach (var entry in entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id), $"Entry missing Id");
            Assert.False(string.IsNullOrWhiteSpace(entry.Query), $"Entry {entry.Id} missing Query");
            Assert.NotNull(entry.ExpectedKeywords);
            Assert.NotNull(entry.ExpectedChunkTypes);
            Assert.False(string.IsNullOrWhiteSpace(entry.Role), $"Entry {entry.Id} missing Role");
        }
    }
}
