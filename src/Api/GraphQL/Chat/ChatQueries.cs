using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Interfaces;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.GraphQL.Chat;

[ExtendObjectType(typeof(global::Query))]
public sealed class ChatQueries
{
    [Authorize]
    public async Task<IReadOnlyList<ChatSessionSummaryType>> GetChatSessionsAsync(
        int limit,
        IChatService chatService,
        ICurrentTenant currentTenant,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        return await LoadChatSessionsAsync(limit, chatService, currentTenant, context, cancellationToken);
    }

    [Authorize]
    [GraphQLName("getChatSessions")]
    public Task<IReadOnlyList<ChatSessionSummaryType>> GetChatSessionsLegacyAsync(
        int limit,
        IChatService chatService,
        ICurrentTenant currentTenant,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        return LoadChatSessionsAsync(limit, chatService, currentTenant, context, cancellationToken);
    }

    [Authorize]
    public async Task<IReadOnlyList<ChatMessageType>> GetChatHistoryAsync(
        Guid sessionId,
        IChatService chatService,
        ICurrentTenant currentTenant,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        return await LoadChatHistoryAsync(sessionId, chatService, currentTenant, context, cancellationToken);
    }

    [Authorize]
    [GraphQLName("getChatHistory")]
    public Task<IReadOnlyList<ChatMessageType>> GetChatHistoryLegacyAsync(
        Guid sessionId,
        IChatService chatService,
        ICurrentTenant currentTenant,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        return LoadChatHistoryAsync(sessionId, chatService, currentTenant, context, cancellationToken);
    }

    private static Guid? ResolveMembershipId(IResolverContext context, ICurrentTenant currentTenant)
    {
        if (currentTenant.MembershipId.HasValue)
            return currentTenant.MembershipId.Value;

        var httpContextAccessor = context.Service<IHttpContextAccessor>();
        var user = httpContextAccessor.HttpContext?.User;
        var membershipIdClaim = user?.FindFirst("MembershipId")?.Value;

        if (Guid.TryParse(membershipIdClaim, out var membershipId))
            return membershipId;

        return null;
    }

    private static async Task<IReadOnlyList<ChatSessionSummaryType>> LoadChatSessionsAsync(
        int limit,
        IChatService chatService,
        ICurrentTenant currentTenant,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = ResolveMembershipId(context, currentTenant);
        if (!membershipId.HasValue)
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));

        var result = await chatService.GetSessionsAsync(membershipId.Value, limit, cancellationToken);

        return result.Select(s => new ChatSessionSummaryType
        {
            Id = s.Id,
            Title = s.Title,
            MessageCount = s.MessageCount,
            LastMessageAt = s.LastMessageAt
        }).ToList();
    }

    private static async Task<IReadOnlyList<ChatMessageType>> LoadChatHistoryAsync(
        Guid sessionId,
        IChatService chatService,
        ICurrentTenant currentTenant,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = ResolveMembershipId(context, currentTenant);
        if (!membershipId.HasValue)
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));

        var result = await chatService.GetHistoryAsync(sessionId, membershipId.Value, cancellationToken);

        return result.Select(m => new ChatMessageType
        {
            Id = m.Id,
            SessionId = m.SessionId,
            SenderId = m.SenderId,
            Role = m.Role.ToString(),
            Content = m.Content,
            TokenCount = m.TokenCount,
            CreatedAt = m.CreatedAt
        }).ToList();
    }
}
