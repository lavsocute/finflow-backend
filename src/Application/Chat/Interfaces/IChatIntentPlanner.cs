namespace FinFlow.Application.Chat.Interfaces;

public interface IChatIntentPlanner
{
    Task<ChatIntentClassification> ClassifyAsync(
        ChatIntentPlanningRequest request,
        CancellationToken ct = default);
}

public sealed record ChatIntentPlanningRequest(
    string Query,
    DateOnly Today);
