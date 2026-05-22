using System.Security.Claims;
using FinFlow.Application.Documents.Commands.ApproveReviewedDocument;
using FinFlow.Application.Documents.DTOs.Responses;
using FinFlow.Application.Documents.Commands.SubmitReviewedDocument;
using FinFlow.Application.Documents.Commands.RejectReviewedDocument;
using FinFlow.Application.Documents.Commands.UploadDocumentForReview;
using FinFlow.Application.Documents.Commands.SaveManualDraft;
using FinFlow.Application.Documents.Commands.SaveReviewedOcrDraft;
using FinFlow.Application.Documents.Commands.ReindexReviewedDocuments;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using HotChocolate.Authorization;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using MediatR;
using Microsoft.AspNetCore.Http;
using DomainError = FinFlow.Domain.Abstractions.Error;

namespace FinFlow.Api.GraphQL.Documents;

public sealed record UploadDocumentForReviewInput(
    string FileName,
    string ContentType,
    string Base64Content);

public sealed record SubmitReviewedDocumentLineItemInput(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

public sealed record SubmitReviewedDocumentInput(
    Guid? DraftId,
    string OriginalFileName,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string? Source,
    string? ConfidenceLabel,
    IReadOnlyList<SubmitReviewedDocumentLineItemInput> LineItems,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null);

public sealed record ApproveReviewedDocumentInput(Guid DocumentId, string? Comment, string? OverrideJustification = null);

public sealed record RejectReviewedDocumentInput(Guid DocumentId, string Reason, string? Comment);

public sealed record SaveManualDraftLineItemInput(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

public sealed record SaveManualDraftInput(
    string OriginalFileName,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    IReadOnlyList<SaveManualDraftLineItemInput> LineItems,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null);

public sealed record SaveManualDraftPayload(Guid DraftId);

public sealed record SaveReviewedOcrDraftLineItemInput(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

public sealed record SaveReviewedOcrDraftInput(
    Guid DraftId,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string? ConfidenceLabel,
    IReadOnlyList<SaveReviewedOcrDraftLineItemInput> LineItems);

public sealed record SaveReviewedOcrDraftPayload(Guid DraftId);

public sealed record ReindexReviewedDocumentsInput(Guid? DocumentId);

public sealed record UpdateDocumentDraftLineItemInput(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal? DiscountPercent,
    decimal DiscountAmount,
    decimal Total);

public sealed record UpdateDocumentDraftInput(
    Guid DraftId,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal? DocumentDiscountPercent,
    decimal DocumentDiscountAmount,
    decimal Vat,
    decimal TotalAmount,
    string? ConfidenceLabel,
    IReadOnlyList<UpdateDocumentDraftLineItemInput> LineItems,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null);

public sealed record ReindexReviewedDocumentsPayload(
    int ScannedDocuments,
    int IndexedDocuments,
    int FailedDocuments,
    int TotalChunks);

public sealed record DocumentOcrDraftLineItemPayload(
    string ItemName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total);

public sealed record DocumentOcrDraftPayload(
    Guid DocumentId,
    string OriginalFileName,
    string ContentType,
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    string Category,
    string VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string Source,
    string ReviewedByStaff,
    string ConfidenceLabel,
    bool HasImage,
    IReadOnlyList<DocumentOcrDraftLineItemPayload> LineItems,
    string CurrencyCode = "VND",
    decimal ExchangeRate = 1m,
    string BaseCurrencyCode = "VND",
    decimal TotalAmountInBaseCurrency = 0m,
    int? ProcessedPageCount = null);

public sealed record ReviewedDocumentPayload(
    Guid DocumentId,
    string Status,
    DateTime SubmittedAt,
    string VendorName,
    string Reference,
    decimal TotalAmount,
    string ReviewedByStaff,
    string CurrencyCode = "VND",
    decimal ExchangeRate = 1m,
    string BaseCurrencyCode = "VND",
    decimal TotalAmountInBaseCurrency = 0m);

[ExtendObjectType(typeof(FinFlow.Api.GraphQL.Auth.AuthMutations))]
public sealed class DocumentsMutations
{
    [Authorize]
    public async Task<DocumentOcrDraftPayload> UploadDocumentForReviewAsync(
        UploadDocumentForReviewInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var fileContents = DecodeBase64(input.Base64Content);
        var accountId = GetRequiredGuidClaim(context, "sub", ClaimTypes.NameIdentifier, unauthorizedMessage: "User is not authenticated or token is invalid");
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var email = GetRequiredClaim(context, "email", ClaimTypes.Email, "User is not authenticated or token is invalid");

        var result = await mediator.Send(
            new UploadDocumentForReviewCommand(
                accountId,
                tenantId,
                membershipId,
                email,
                input.FileName,
                input.ContentType,
                fileContents),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToDocumentOcrDraftPayload(result.Value);
    }

    [Authorize]
    public async Task<ReviewedDocumentPayload> SubmitReviewedDocumentAsync(
        SubmitReviewedDocumentInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var reviewedByStaff = GetRequiredClaim(context, "email", ClaimTypes.Email, "User is not authenticated or token is invalid");

        var result = await mediator.Send(
            new SubmitReviewedDocumentCommand(
                input.DraftId,
                tenantId,
                membershipId,
                input.OriginalFileName,
                input.VendorName,
                input.Reference,
                input.DocumentDate,
                input.Category,
                input.VendorTaxId,
                input.Subtotal,
                input.Vat,
                input.TotalAmount,
                string.IsNullOrWhiteSpace(input.Source) ? "staff-upload" : input.Source,
                reviewedByStaff,
                string.IsNullOrWhiteSpace(input.ConfidenceLabel) ? "Staff corrected" : input.ConfidenceLabel,
                DateTime.UtcNow,
                input.LineItems.Select(x => new SubmitReviewedDocumentLineItem(x.ItemName, x.Quantity, x.UnitPrice, x.Total)).ToList(),
                CurrencyCode: input.CurrencyCode,
                ExchangeRate: input.ExchangeRate),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return new ReviewedDocumentPayload(
            result.Value.DocumentId,
            result.Value.Status,
            result.Value.SubmittedAt,
            result.Value.VendorName,
            result.Value.Reference,
            result.Value.TotalAmount,
            result.Value.ReviewedByStaff,
            result.Value.CurrencyCode,
            result.Value.ExchangeRate,
            result.Value.BaseCurrencyCode,
            result.Value.TotalAmountInBaseCurrency);
    }

    [Authorize]
    public async Task<SaveManualDraftPayload> SaveManualDraftAsync(
        SaveManualDraftInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var reviewedByStaff = GetRequiredClaim(context, "email", ClaimTypes.Email, "User is not authenticated or token is invalid");

        var result = await mediator.Send(
            new SaveManualDraftCommand(
                tenantId,
                membershipId,
                input.OriginalFileName,
                input.VendorName,
                input.Reference,
                input.DocumentDate,
                input.Category,
                input.VendorTaxId,
                input.Subtotal,
                input.Vat,
                input.TotalAmount,
                reviewedByStaff,
                input.LineItems.Select(x => new SaveManualDraftLineItem(x.ItemName, x.Quantity, x.UnitPrice, x.Total)).ToList(),
                CurrencyCode: input.CurrencyCode,
                ExchangeRate: input.ExchangeRate),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return new SaveManualDraftPayload(result.Value);
    }

    [Authorize]
    public async Task<SaveReviewedOcrDraftPayload> SaveReviewedOcrDraftAsync(
        SaveReviewedOcrDraftInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");

        var result = await mediator.Send(
            new SaveReviewedOcrDraftCommand(
                input.DraftId,
                tenantId,
                membershipId,
                input.VendorName,
                input.Reference,
                input.DocumentDate,
                input.Category,
                input.VendorTaxId,
                input.Subtotal,
                input.Vat,
                input.TotalAmount,
                string.IsNullOrWhiteSpace(input.ConfidenceLabel) ? "Staff corrected" : input.ConfidenceLabel,
                input.LineItems.Select(x => new SaveReviewedOcrDraftLineItem(x.ItemName, x.Quantity, x.UnitPrice, x.Total)).ToList()),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return new SaveReviewedOcrDraftPayload(result.Value);
    }

    [Authorize]
    public async Task<ReviewedDocumentPayload> ApproveReviewedDocumentAsync(
        ApproveReviewedDocumentInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureApproverRole(context);
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var role = GetRequiredRole(context);

        var result = await mediator.Send(new ApproveReviewedDocumentCommand(input.DocumentId, tenantId, membershipId, role, input.Comment, input.OverrideJustification), cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToReviewedDocumentPayload(result.Value);
    }

    [Authorize]
    public async Task<ReviewedDocumentPayload> RejectReviewedDocumentAsync(
        RejectReviewedDocumentInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureApproverRole(context);
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");

        var result = await mediator.Send(new RejectReviewedDocumentCommand(input.DocumentId, tenantId, membershipId, input.Reason, input.Comment), cancellationToken);
        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToReviewedDocumentPayload(result.Value);
    }

    [Authorize]
    public async Task<ReindexReviewedDocumentsPayload> ReindexReviewedDocumentsAsync(
        ReindexReviewedDocumentsInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        EnsureTenantAdminRole(context);
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");

        var result = await mediator.Send(
            new ReindexReviewedDocumentsCommand(tenantId, input.DocumentId),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return new ReindexReviewedDocumentsPayload(
            result.Value.ScannedDocuments,
            result.Value.IndexedDocuments,
            result.Value.FailedDocuments,
            result.Value.TotalChunks);
    }

    private static byte[] DecodeBase64(string base64Content)
    {
        if (string.IsNullOrWhiteSpace(base64Content))
            throw ToGraphQlException(new DomainError("Documents.Base64ContentRequired", "File content is required."));

        try
        {
            return Convert.FromBase64String(base64Content);
        }
        catch (FormatException)
        {
            throw ToGraphQlException(new DomainError("Documents.Base64ContentInvalid", "File content must be valid Base64."));
        }
    }

    internal static Guid GetRequiredGuidClaim(
        IResolverContext context,
        string claimType,
        string? fallbackClaimType = null,
        string unauthorizedMessage = "User is not authenticated or token is invalid")
    {
        var rawValue = GetOptionalClaim(context, claimType, fallbackClaimType);
        if (Guid.TryParse(rawValue, out var value))
            return value;

        throw new GraphQLException(new HotChocolate.Error(unauthorizedMessage, "Account.Unauthorized"));
    }

    internal static string GetRequiredClaim(
        IResolverContext context,
        string claimType,
        string? fallbackClaimType,
        string unauthorizedMessage)
    {
        var value = GetOptionalClaim(context, claimType, fallbackClaimType);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        throw new GraphQLException(new HotChocolate.Error(unauthorizedMessage, "Account.Unauthorized"));
    }

    internal static RoleType GetRequiredRole(IResolverContext context)
    {
        var rawRole = GetOptionalClaim(context, ClaimTypes.Role, "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
        if (Enum.TryParse<RoleType>(rawRole, out var role))
            return role;

        throw new GraphQLException(new HotChocolate.Error("The current user is not authorized to access this resource.", "Account.Unauthorized"));
    }

    internal static void EnsureApproverRole(IResolverContext context)
    {
        var role = GetRequiredRole(context);
        if (role is RoleType.Manager or RoleType.Accountant or RoleType.TenantAdmin)
            return;

        throw ToGraphQlException(ReviewedDocumentErrors.ForbiddenApproval);
    }

    internal static void EnsureTenantAdminRole(IResolverContext context)
    {
        var role = GetRequiredRole(context);
        if (role == RoleType.TenantAdmin)
            return;

        throw new GraphQLException(new HotChocolate.Error("The current user is not authorized to access this resource.", "Account.Unauthorized"));
    }

    private static string? GetOptionalClaim(IResolverContext context, string claimType, string? fallbackClaimType = null)
    {
        var user = context.Service<IHttpContextAccessor>().HttpContext?.User;
        return user?.FindFirst(claimType)?.Value
            ?? (fallbackClaimType is null ? null : user?.FindFirst(fallbackClaimType)?.Value);
    }

    private static ReviewedDocumentPayload ToReviewedDocumentPayload(ReviewedDocumentResponse response) =>
        new(
            response.DocumentId,
            response.Status,
            response.SubmittedAt,
            response.VendorName,
            response.Reference,
            response.TotalAmount,
            response.ReviewedByStaff);

    internal static DocumentOcrDraftPayload ToDocumentOcrDraftPayload(DocumentOcrDraftResponse response) =>
        new(
            response.DocumentId,
            response.OriginalFileName,
            response.ContentType,
            response.VendorName,
            response.Reference,
            response.DocumentDate,
            response.Category,
            response.VendorTaxId,
            response.Subtotal,
            response.Vat,
            response.TotalAmount,
            response.Source,
            response.ReviewedByStaff,
            response.ConfidenceLabel,
            response.HasImage,
            response.LineItems
                .Select(x => new DocumentOcrDraftLineItemPayload(x.ItemName, x.Quantity, x.UnitPrice, x.Total))
                .ToList(),
            response.CurrencyCode,
            response.ExchangeRate,
            response.BaseCurrencyCode,
            response.TotalAmountInBaseCurrency,
            response.ProcessedPageCount);

    private static GraphQLException ToGraphQlException(DomainError error) =>
        new(new HotChocolate.Error(error.Description, error.Code));

    // ─── New mutations: UpdateDocumentDraft, DeleteDocumentDraft, WithdrawReviewedDocument ───

    [Authorize]
    public async Task<DocumentOcrDraftPayload> UpdateDocumentDraftAsync(
        UpdateDocumentDraftInput input,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var isTenantOwner = IsTenantOwnerRole(context);

        var result = await mediator.Send(
            new Application.Documents.Commands.UpdateDocumentDraft.UpdateDocumentDraftCommand(
                input.DraftId,
                tenantId,
                membershipId,
                isTenantOwner,
                input.VendorName,
                input.Reference,
                input.DocumentDate,
                input.Category,
                input.VendorTaxId,
                input.Subtotal,
                input.DocumentDiscountPercent,
                input.DocumentDiscountAmount,
                input.Vat,
                input.TotalAmount,
                input.ConfidenceLabel ?? "Staff corrected",
                input.LineItems.Select(x => new Application.Documents.Commands.UpdateDocumentDraft.UpdateDocumentDraftLineItem(
                    x.ItemName, x.Quantity, x.UnitPrice, x.DiscountPercent, x.DiscountAmount, x.Total)).ToList(),
                CurrencyCode: input.CurrencyCode,
                ExchangeRate: input.ExchangeRate),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToDocumentOcrDraftPayload(result.Value);
    }

    [Authorize]
    public async Task<bool> DeleteDocumentDraftAsync(
        Guid draftId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var isTenantOwner = IsTenantOwnerRole(context);

        var result = await mediator.Send(
            new Application.Documents.Commands.DeleteDocumentDraft.DeleteDocumentDraftCommand(
                draftId, tenantId, membershipId, isTenantOwner),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return true;
    }

    [Authorize]
    public async Task<ReviewedDocumentPayload> WithdrawReviewedDocumentAsync(
        Guid documentId,
        [Service] IMediator mediator,
        IResolverContext context,
        CancellationToken cancellationToken)
    {
        var tenantId = GetRequiredGuidClaim(context, "IdTenant", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var membershipId = GetRequiredGuidClaim(context, "MembershipId", unauthorizedMessage: "The current user is not authorized to access this resource.");
        var isTenantOwner = IsTenantOwnerRole(context);

        var result = await mediator.Send(
            new Application.Documents.Commands.WithdrawReviewedDocument.WithdrawReviewedDocumentCommand(
                documentId, tenantId, membershipId, isTenantOwner),
            cancellationToken);

        if (result.IsFailure)
            throw ToGraphQlException(result.Error);

        return ToReviewedDocumentPayload(result.Value);
    }

    private static bool IsTenantOwnerRole(IResolverContext context)
    {
        var rawRole = GetOptionalClaim(context, ClaimTypes.Role, "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
        return Enum.TryParse<RoleType>(rawRole, out var role) && role == RoleType.TenantAdmin;
    }
}
