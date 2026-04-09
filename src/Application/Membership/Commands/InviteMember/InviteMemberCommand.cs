using FinFlow.Application.Common;
using FinFlow.Application.Membership.DTOs.Requests;
using FinFlow.Application.Membership.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Membership.Commands.InviteMember;

public record InviteMemberCommand(InviteMemberRequest Request)
    : ICommand<Result<InvitationResponse>>;
