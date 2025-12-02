using FluentValidation;
using BLL.Models.Tasks;

namespace BLL.Validators;

public class TaskDtoValidator : AbstractValidator<TaskDto>
{
    public TaskDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Task name is required")
            .MaximumLength(100).WithMessage("Task name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters");

        RuleFor(x => x.State)
            .NotEmpty().WithMessage("Task state is required");
    }
}
