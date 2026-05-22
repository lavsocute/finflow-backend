using FinFlow.Api.GraphQL.Auth;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Interfaces;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinFlow.Api.GraphQL.Chat;

[ExtendObjectType(typeof(AuthMutations))]
public class ChatMutations
{
    [Authorize]
    public async Task<ChatResponseType> ChatAsync(
        ChatInput input,
        IChatService chatService,
        ICurrentTenant currentTenant,
        IResolverContext context,
        ILogger<ChatMutations> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ChatAsync called. Query: {Query}, SessionId: {SessionId}", input.Query, input.SessionId);
        var membershipId = ResolveMembershipId(context, currentTenant);
        var tenantId = currentTenant.Id ?? Guid.Empty;

        if (!membershipId.HasValue)
            throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));

        if (tenantId == Guid.Empty)
            throw new GraphQLException(new HotChocolate.Error("Tenant not found", "Tenant.NotFound"));

        var request = new ChatRequest(
            membershipId.Value,
            tenantId,
            input.SessionId,
            input.Query,
            input.DepartmentId);

        ChatResponse result;
        try
        {
            result = await chatService.ChatAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsForbiddenChatScopeError(ex))
        {
            logger.LogWarning(
                ex,
                "ChatAsync denied for membership {MembershipId} in tenant {TenantId}.",
                membershipId.Value,
                tenantId);
            throw new GraphQLException(new HotChocolate.Error(ex.Message, "Chat.Forbidden"));
        }

        return new ChatResponseType(
            result.Answer,
            result.SessionId,
            result.MessageId,
            result.DocumentCount,
            result.TokenUsage,
            result.AnswerSource,
            result.Citations?.Select(c => new ChatCitationType(c.ChunkNumber, c.ChunkId, c.DocumentId, c.ChunkType, c.Preview)).ToList()
                ?? new List<ChatCitationType>());
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

    private static bool IsForbiddenChatScopeError(InvalidOperationException exception) =>
        exception.Message.StartsWith("Chat access denied:", StringComparison.Ordinal);
}

public sealed record ChatInput(
    Guid? SessionId,
    string Query,
    Guid? DepartmentId
);

public sealed record ChatResponseType(
    string Answer,
    Guid SessionId,
    Guid MessageId,
    int DocumentCount,
    int TokenUsage,
    ChatAnswerSource AnswerSource,
    IReadOnlyList<ChatCitationType> Citations
);

public sealed record ChatCitationType(
    int ChunkNumber,
    Guid ChunkId,
    Guid DocumentId,
    string ChunkType,
    string Preview);
