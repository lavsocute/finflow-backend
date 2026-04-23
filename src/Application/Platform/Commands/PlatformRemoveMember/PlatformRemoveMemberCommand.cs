using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Platform.Commands.PlatformRemoveMember;

public sealed record PlatformRemoveMemberCommand(
    Guid MembershipId,
    string Reason) : ICommand<Result>;
