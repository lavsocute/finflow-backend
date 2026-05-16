using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Documents.Commands.DeleteDocumentDraft;

internal sealed class DeleteDocumentDraftCommandHandler
    : IRequestHandler<DeleteDocumentDraftCommand, Result<Unit>>
{
    private readonly IUploadedDocumentDraftRepository _draftRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteDocumentDraftCommandHandler> _logger;

    public DeleteDocumentDraftCommandHandler(
        IUploadedDocumentDraftRepository draftRepo,
        IUnitOfWork unitOfWork,
        ILogger<DeleteDocumentDraftCommandHandler> logger)
    {
        _draftRepo = draftRepo;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(DeleteDocumentDraftCommand cmd, CancellationToken ct)
    {
        var draft = cmd.IsTenantOwner
            ? await _draftRepo.GetByTenantIdAsync(cmd.DraftId, cmd.TenantId, ct)
            : await _draftRepo.GetByIdAsync(cmd.DraftId, cmd.TenantId, cmd.MembershipId, ct);

        if (draft is null)
            return Result.Failure<Unit>(UploadedDocumentDraftErrors.NotFound);

        var deleteResult = draft.SoftDelete();
        if (deleteResult.IsFailure)
            return Result.Failure<Unit>(deleteResult.Error);

        _draftRepo.Update(draft);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Document draft soft-deleted: {DraftId} tenant={TenantId} membership={MembershipId} byTenantOwner={IsTenantOwner}",
            draft.Id, cmd.TenantId, cmd.MembershipId, cmd.IsTenantOwner);

        return Result.Success(Unit.Value);
    }
}
