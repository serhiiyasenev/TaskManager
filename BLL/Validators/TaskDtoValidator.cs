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

        RuleFor(x => x.ReminderOffsetMinutes)
            .InclusiveBetween(1, 7 * 24 * 60).When(x => x.ReminderEnabled && x.ReminderOffsetMinutes.HasValue)
            .WithMessage("Reminder offset must be between 1 minute and 7 days.");

        RuleFor(x => x.EscalationDelayMinutes)
            .InclusiveBetween(1, 7 * 24 * 60).When(x => x.EscalationEnabled && x.EscalationDelayMinutes.HasValue)
            .WithMessage("Escalation delay must be between 1 minute and 7 days.");
    }
}
