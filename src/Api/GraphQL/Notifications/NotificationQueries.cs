using FinFlow.Domain.Notifications;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;

namespace FinFlow.Api.GraphQL.Notifications;

[ExtendObjectType(typeof(global::Query))]
public sealed class NotificationQueries
{
    [Authorize]
    public async Task<IReadOnlyList<NotificationPayload>> MyNotificationsAsync(
        bool? unreadOnly,
        int? limit,
        [Service] INotificationRepository repo,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = ResolveMembership(http);
        var resolvedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var rows = await repo.GetByMembershipIdAsync(membershipId, unreadOnly ?? false, resolvedLimit, cancellationToken);
        return rows.Select(NotificationPayload.From).ToList();
    }

    [Authorize]
    public Task<int> UnreadNotificationCountAsync(
        [Service] INotificationRepository repo,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = ResolveMembership(http);
        return repo.CountUnreadAsync(membershipId, cancellationToken);
    }

    private static Guid ResolveMembership(IHttpContextAccessor http)
    {
        var raw = http.HttpContext?.User?.FindFirst("MembershipId")?.Value;
        if (Guid.TryParse(raw, out var id) && id != Guid.Empty)
            return id;
        throw new GraphQLException(new HotChocolate.Error("Membership context missing.", "Account.Unauthorized"));
    }
}
