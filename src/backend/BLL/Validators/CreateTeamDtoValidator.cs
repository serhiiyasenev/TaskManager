using FluentValidation;
using BLL.Models.Teams;

namespace BLL.Validators;

public class CreateTeamDtoValidator : AbstractValidator<CreateTeamDto>
{
    public CreateTeamDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Team name is required")
            .MaximumLength(100).WithMessage("Team name cannot exceed 100 characters");
    }
}
