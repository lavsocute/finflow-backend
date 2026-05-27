using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

public sealed class RouterBackedChatIntentPlanner : IChatIntentPlanner
{
    private readonly IChatIntentRouter _router;

    public RouterBackedChatIntentPlanner(IChatIntentRouter router)
    {
        _router = router;
    }

    public Task<ChatIntentClassification> ClassifyAsync(
        ChatIntentPlanningRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(_router.Classify(request.Query));
}
