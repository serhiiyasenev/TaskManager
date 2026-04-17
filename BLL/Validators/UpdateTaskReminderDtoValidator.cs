using BLL.Models.Tasks;
using FluentValidation;

namespace BLL.Validators;

public class UpdateTaskReminderDtoValidator : AbstractValidator<UpdateTaskReminderDto>
{
    private const int MaxMinutes = 7 * 24 * 60;

    public UpdateTaskReminderDtoValidator()
    {
        RuleFor(x => x.ReminderOffsetMinutes)
            .GreaterThan(0).When(x => x.ReminderEnabled && x.ReminderOffsetMinutes.HasValue)
            .LessThanOrEqualTo(MaxMinutes).When(x => x.ReminderOffsetMinutes.HasValue)
            .WithMessage("Reminder offset must be between 1 minute and 7 days.");

        RuleFor(x => x.EscalationDelayMinutes)
            .GreaterThan(0).When(x => x.EscalationEnabled && x.EscalationDelayMinutes.HasValue)
            .LessThanOrEqualTo(MaxMinutes).When(x => x.EscalationDelayMinutes.HasValue)
            .WithMessage("Escalation delay must be between 1 minute and 7 days.");

        RuleFor(x => x.DueDate)
            .GreaterThan(_ => DateTime.UtcNow.AddMinutes(-1))
            .When(x => x.DueDate.HasValue)
            .WithMessage("Due date must be in the future.");
    }
}
