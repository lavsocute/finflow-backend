using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application.Auth.DTOs.Requests;
using FluentValidation;

namespace FinFlow.Application.Auth.Validators;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Request.Password).NotEmpty();
        RuleFor(x => x.Request.Name).NotEmpty();
        RuleFor(x => x.Request.TenantCode).NotEmpty();
        RuleFor(x => x.Request.DepartmentName).NotEmpty();
    }
}
