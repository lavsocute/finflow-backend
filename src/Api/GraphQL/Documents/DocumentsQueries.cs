using System.Security.Claims;
using FinFlow.Application.Documents.Queries.GetApprovalDetail;
using FinFlow.Application.Documents.Queries.GetApprovalQueue;
using FinFlow.Application.Documents.Queries.GetMyDocumentDraft;
using FinFlow.Application.Documents.Queries.GetMyDocumentDrafts;
using FinFlow.Application.Documents.Queries.GetMySubmittedDocument;
using FinFlow.Application.Documents.Queries.GetMySubmittedDocuments;
using FinFlow.Application.Documents.Queries.GetPendingApprovalItems;
using FinFlow.Application.Documents.Queries.ExportApprovalQueue;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.GraphQL.Documents;

public sealed record PendingApprovalItemPayload(
    Guid DocumentId,
    string Title,
    string VendorName,
    string Requester,
    string RequesterEmail,
    string Department,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    DateTime SubmittedAt,
    string Priority,
    string Status,
    string? PolicySummary);

public sealed record ApprovalDetailPayload(
    Guid DocumentId,
    string RequestCode,
    string Title,
    string VendorName,
    string RequesterName,
    string RequesterEmail,
    string Department,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    DateTime SubmittedAt,
    string Priority,
    string Status,
    string? PolicySummary,
    IReadOnlyList<ApprovalLineItemPayload> LineItems);

public sealed record ApprovalLineItemPayload(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

public sealed record MyDocumentDraftPayload(
    Guid DocumentId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    string Category,
    string Source,
    string ConfidenceLabel,
    string OwnerEmail,
    DateTime UploadedAt);

public sealed record MySubmittedDocumentPayload(
    Guid DocumentId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    string Category,
    string Source,
    string Status,
    string SubmittedByEmail,
    DateTime SubmittedAt,
    DateTime LastUpdatedAt,
    string? RejectionReason);

public sealed record MySubmittedDocumentLineItemPayload(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

public sealed record MySubmittedDocumentDetailPayload(
    Guid DocumentId,
    string OriginalFileName,
    string ContentType,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    DateOnly DueDate,
    string Category,
    string VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string Source,
    string Status,
    string SubmittedByEmail,
    DateTime SubmittedAt,
    DateTime LastUpdatedAt,
    string? RejectionReason,
    IReadOnlyList<MySubmittedDocumentLineItemPayload> LineItems);

public sealed record ApprovalQueueItemPayload(
    Guid DocumentId,
    string Title,
    string VendorName,
    string Requester,
    string RequesterEmail,
    string Department,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    DateTime SubmittedAt,
    string Priority,
    string Status,
    string? PolicySummary);

public sealed record ApprovalQueuePayload(
    IReadOnlyList<ApprovalQueueItemPayload> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ExportApprovalQueuePayload(
    string FileName,
    string DownloadUrl);

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
                x.VendorName,
                x.Requester,
                x.RequesterEmail,
                x.Department,
                x.Amount,
                x.Currency,
                x.DueDate,
                x.SubmittedAt,
                x.Priority,
                x.Status,
                x.PolicySummary))
            .ToList();
    }

    [Authorize]
    public async Task<ApprovalDetailPayload?> ApprovalDetailAsync(
        Guid documentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        DocumentsMutations.EnsureApproverRole(context);
        var tenantId = EnsureAuthorizedTenant(context);

        var result = await mediator.Send(new GetApprovalDetailQuery(tenantId, documentId), cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        if (result.Value is null)
            return null;

        var detail = result.Value;
        return new ApprovalDetailPayload(
            detail.DocumentId,
            detail.RequestCode,
            detail.Title,
            detail.VendorName,
            detail.RequesterName,
            detail.RequesterEmail,
            detail.Department,
            detail.Amount,
            detail.Currency,
            detail.DueDate,
            detail.SubmittedAt,
            detail.Priority,
            detail.Status,
            detail.PolicySummary,
            detail.LineItems
                .Select(item => new ApprovalLineItemPayload(
                    item.Description,
                    item.Quantity,
                    item.UnitPrice,
                    item.Total))
                .ToList());
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
                x.Category,
                x.Source,
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
                x.Category,
                x.Source,
                x.Status,
                x.SubmittedByEmail,
                x.SubmittedAt,
                x.LastUpdatedAt,
                x.RejectionReason))
            .ToList();
    }

    [Authorize]
    public async Task<MySubmittedDocumentDetailPayload> MySubmittedDocumentAsync(
        Guid documentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var scope = EnsureAuthorizedWorkspace(context);

        var result = await mediator.Send(
            new GetMySubmittedDocumentQuery(scope.TenantId, scope.MembershipId, documentId),
            cancellationToken);

        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        var doc = result.Value;
        return new MySubmittedDocumentDetailPayload(
            doc.DocumentId,
            doc.OriginalFileName,
            doc.ContentType,
            doc.VendorName,
            doc.Reference,
            doc.DocumentDate,
            doc.DueDate,
            doc.Category,
            doc.VendorTaxId,
            doc.Subtotal,
            doc.Vat,
            doc.TotalAmount,
            doc.Source,
            doc.Status,
            doc.SubmittedByEmail,
            doc.SubmittedAt,
            doc.LastUpdatedAt,
            doc.RejectionReason,
            doc.LineItems
                .Select(item => new MySubmittedDocumentLineItemPayload(
                    item.ItemName,
                    item.Quantity,
                    item.UnitPrice,
                    item.Total))
                .ToList());
    }

    [Authorize]
    public async Task<ApprovalQueuePayload> ApprovalQueueAsync(
        ApprovalStatusFilter status,
        string? search,
        int page,
        int pageSize,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        DocumentsMutations.EnsureApproverRole(context);
        var tenantId = EnsureAuthorizedTenant(context);

        var result = await mediator.Send(
            new GetApprovalQueueQuery(tenantId, status, search, page, pageSize),
            cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        var queue = result.Value;
        return new ApprovalQueuePayload(
            queue.Items
                .Select(x => new ApprovalQueueItemPayload(
                    x.DocumentId,
                    x.Title,
                    x.VendorName,
                    x.Requester,
                    x.RequesterEmail,
                    x.Department,
                    x.Amount,
                    x.Currency,
                    x.DueDate,
                    x.SubmittedAt,
                    x.Priority,
                    x.Status,
                    x.PolicySummary))
                .ToList(),
            queue.Page,
            queue.PageSize,
            queue.TotalCount,
            queue.TotalPages);
    }

    [Authorize]
    public async Task<ExportApprovalQueuePayload> ExportApprovalQueueAsync(
        ApprovalStatusFilter status,
        string? search,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        DocumentsMutations.EnsureApproverRole(context);
        var tenantId = EnsureAuthorizedTenant(context);

        var result = await mediator.Send(
            new ExportApprovalQueueQuery(tenantId, status, search),
            cancellationToken);
        if (result.IsFailure)
            throw new GraphQLException(new HotChocolate.Error(result.Error.Description, result.Error.Code));

        return new ExportApprovalQueuePayload(result.Value.FileName, result.Value.DownloadUrl);
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