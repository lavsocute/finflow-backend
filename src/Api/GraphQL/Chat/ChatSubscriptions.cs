using System.Runtime.CompilerServices;
using System.Security.Claims;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Interfaces;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.GraphQL.Chat;

[ExtendObjectType(typeof(FinFlow.Api.GraphQL.SubscriptionType))]
public class ChatSubscriptions
{
    [Authorize]
    public async IAsyncEnumerable<ChatStreamEventType> OnChatStreamAsync(
        ChatInput input,
        [Service] IChatService chatService,
        [Service] ICurrentTenant currentTenant,
        IResolverContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var membershipId = ResolveMembershipId(context, currentTenant)
            ?? throw new GraphQLException(new HotChocolate.Error("User is not authenticated", "Account.Unauthorized"));
        var tenantId = currentTenant.Id ?? Guid.Empty;
        if (tenantId == Guid.Empty)
            throw new GraphQLException(new HotChocolate.Error("Tenant not found", "Tenant.NotFound"));

        var request = new ChatRequest(membershipId, tenantId, input.SessionId, input.Query, input.DepartmentId);

        await foreach (var evt in chatService.ChatStreamAsync(request, cancellationToken))
        {
            yield return new ChatStreamEventType(
                Kind: evt.Kind.ToString(),
                TokenChunk: evt.TokenChunk,
                SessionId: evt.SessionId,
                MessageId: evt.MessageId,
                DocumentCount: evt.DocumentCount,
                TokenUsage: evt.TokenUsage,
                CompleteAnswer: evt.CompleteAnswer);
        }
    }

    private static Guid? ResolveMembershipId(IResolverContext context, ICurrentTenant currentTenant)
    {
        if (currentTenant.MembershipId.HasValue)
            return currentTenant.MembershipId.Value;
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var claim = user?.FindFirst("MembershipId")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

public sealed record ChatStreamEventType(
    string Kind,
    string? TokenChunk,
    Guid? SessionId,
    Guid? MessageId,
    int? DocumentCount,
    int? TokenUsage,
    string? CompleteAnswer);
