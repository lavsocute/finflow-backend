using System.Security.Claims;
using FinFlow.Application.Documents.Queries.GetMyDocumentDraft;
using FinFlow.Application.Documents.Queries.GetMyDocumentDrafts;
using FinFlow.Application.Documents.Queries.GetMySubmittedDocuments;
using FinFlow.Application.Documents.Queries.GetPendingApprovalItems;
using FinFlow.Domain.Abstractions;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.GraphQL.Documents;

public sealed record PendingApprovalItemPayload(
    Guid DocumentId,
    string Title,
    string Requester,
    string Department,
    decimal Amount,
    DateOnly DueDate,
    string Priority,
    string Status);

public sealed record MyDocumentDraftPayload(
    Guid DocumentId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    string ConfidenceLabel,
    string OwnerEmail,
    DateTime UploadedAt);

public sealed record MySubmittedDocumentPayload(
    Guid DocumentId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    string Status,
    string SubmittedByEmail,
    DateTime SubmittedAt,
    DateTime LastUpdatedAt,
    string? RejectionReason);

[ExtendObjectType(typeof(global::Query))]
public sealed class DocumentsQueries
{
    [Authorize]
    public async Task<IReadOnlyList<PendingApprovalItemPayload>> PendingApprovalItemsAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        DocumentsMutations.EnsureApproverRole(context);
        var tenantId = EnsureAuthorizedTenant(context);

        var result = await mediator.Send(new GetPendingApprovalItemsQuery(tenantId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value
            .Select(x => new PendingApprovalItemPayload(
                x.DocumentId,
                x.Title,
                x.Requester,
                x.Department,
                x.Amount,
                x.DueDate,
                x.Priority,
                x.Status))
            .ToList();
    }

    [Authorize]
    public async Task<IReadOnlyList<MyDocumentDraftPayload>> MyDocumentDraftsAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(new GetMyDocumentDraftsQuery(scope.TenantId, scope.MembershipId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value
            .Select(x => new MyDocumentDraftPayload(
                x.DocumentId,
                x.OriginalFileName,
                x.VendorName,
                x.Reference,
                x.TotalAmount,
                x.ConfidenceLabel,
                x.OwnerEmail,
                x.UploadedAt))
            .ToList();
    }

    [Authorize]
    public async Task<DocumentOcrDraftPayload> MyDocumentDraftAsync(
        Guid documentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(
            new GetMyDocumentDraftQuery(scope.TenantId, scope.MembershipId, documentId),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return DocumentsMutations.ToDocumentOcrDraftPayload(result.Value);
    }

    [Authorize]
    public async Task<IReadOnlyList<MySubmittedDocumentPayload>> MySubmittedDocumentsAsync(
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(new GetMySubmittedDocumentsQuery(scope.TenantId, scope.MembershipId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return result.Value
            .Select(x => new MySubmittedDocumentPayload(
                x.DocumentId,
                x.OriginalFileName,
                x.VendorName,
                x.Reference,
                x.TotalAmount,
                x.Status,
                x.SubmittedByEmail,
                x.SubmittedAt,
                x.LastUpdatedAt,
                x.RejectionReason))
            .ToList();
    }

    private static Guid EnsureAuthorizedTenant(IResolverContext context)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        var accountIdRaw = user?.FindFirst("sub")?.Value ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantIdRaw = user?.FindFirst("IdTenant")?.Value;

        if (!Guid.TryParse(accountIdRaw, out _) || !Guid.TryParse(tenantIdRaw, out var tenantId))
            throw new GraphQLException(new HotChocolate.Error("The current user is not authorized to access this resource.", "Account.Unauthorized"));

        return tenantId;
    }

    private static (Guid TenantId, Guid MembershipId) EnsureAuthorizedWorkspace(IResolverContext context)
    {
        var tenantId = DocumentsMutations.GetRequiredGuidClaim(
            context,
            "IdTenant",
            unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = DocumentsMutations.GetRequiredGuidClaim(
            context,
            "MembershipId",
            unauthorizedMessage: "The current user is not authorized to access this resource.");

        return (tenantId, membershipId);
    }
}
