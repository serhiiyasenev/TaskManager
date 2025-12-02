using FluentValidation;
using BLL.Models.Projects;

namespace BLL.Validators;

public class ProjectDtoValidator : AbstractValidator<ProjectDto>
{
    public ProjectDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required")
            .MaximumLength(100).WithMessage("Project name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters");

        RuleFor(x => x.Deadline)
            .GreaterThan(DateTime.UtcNow).WithMessage("Deadline must be in the future")
            .When(x => x.Deadline != default);
    }
}
