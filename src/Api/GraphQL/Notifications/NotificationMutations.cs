using FinFlow.Api.GraphQL.Auth;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Notifications;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;

namespace FinFlow.Api.GraphQL.Notifications;

[ExtendObjectType(typeof(AuthMutations))]
public sealed class NotificationMutations
{
    /// <summary>
    /// Mark a single notification as read. Idempotent — calling on an
    /// already-read notification is a no-op.
    /// </summary>
    [Authorize]
    public async Task<bool> MarkNotificationAsReadAsync(
        Guid notificationId,
        [Service] INotificationRepository repo,
        [Service] IUnitOfWork unitOfWork,
        [Service] IHttpContextAccessor http,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var membershipId = ResolveMembership(http);

        var entity = await repo.GetEntityByIdAsync(notificationId, membershipId, cancellationToken);
        if (entity is null)
            throw new GraphQLException(new HotChocolate.Error(
                NotificationErrors.NotFound.Description, NotificationErrors.NotFound.Code));

        var result = entity.MarkAsRead();
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        repo.Update(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Bulk-mark all unread notifications as read. Returns count of rows updated.
    /// </summary>
    [Authorize]
    public Task<int> MarkAllNotificationsAsReadAsync(
        [Service] INotificationRepository repo,
        IResolverContext context,
        [Service] IHttpContextAccessor http,
        CancellationToken cancellationToken)
    {
        var membershipId = ResolveMembership(http);
        return repo.MarkAllAsReadAsync(membershipId, cancellationToken);
    }

    private static Guid ResolveMembership(IHttpContextAccessor http)
    {
        var raw = http.HttpContext?.User?.FindFirst("MembershipId")?.Value;
        if (Guid.TryParse(raw, out var id) && id != Guid.Empty)
            return id;
        throw new GraphQLException(new HotChocolate.Error("Membership context missing.", "Account.Unauthorized"));
    }
}
