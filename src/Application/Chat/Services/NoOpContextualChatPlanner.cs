using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

public sealed class NoOpContextualChatPlanner : IContextualChatPlanner
{
    public Task<ContextualChatPlan?> PlanAsync(
        ContextualChatPlanRequest request,
        CancellationToken ct = default) =>
        Task.FromResult<ContextualChatPlan?>(null);
}
