using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Documents.Commands.DeleteDocumentDraft;

public sealed record DeleteDocumentDraftCommand(
    Guid DraftId,
    Guid TenantId,
    Guid MembershipId,
    bool IsTenantOwner) : IRequest<Result<Unit>>;
