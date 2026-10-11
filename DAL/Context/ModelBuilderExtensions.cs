using DAL.Entities;
using DAL.Enum;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Task = DAL.Entities.Task;

namespace DAL.Context
{
    public static class ModelBuilderExtensions
    {
        public static void Configure(this ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Project>()
                .HasMany(p => p.Tasks)
                .WithOne(t => t.Project)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Project>()
                .HasOne(p => p.Author)
                .WithMany()
                .HasForeignKey(p => p.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Project>()
                .HasOne(p => p.Team)
                .WithMany()
                .HasForeignKey(p => p.TeamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Project>()
                .HasIndex(p => new { p.TeamId, p.Name })
                .IsUnique();

            modelBuilder.Entity<Task>()
                .HasOne(t => t.Performer)
                .WithMany(u => u.Tasks)
                .HasForeignKey(t => t.PerformerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Task>()
                .HasOne(t => t.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Team>()
                .HasMany(t => t.Projects)
                .WithOne(p => p.Team)
                .HasForeignKey(p => p.TeamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Team>()
                .HasMany(t => t.Users)
                .WithOne(u => u.Team)
                .HasForeignKey(u => u.TeamId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Team>()
                .HasIndex(t => t.Name)
                .IsUnique();

            modelBuilder.Entity<Team>().Property(team => team.OwnerId).IsConcurrencyToken();
            modelBuilder.Entity<Team>().HasOne<User>().WithMany().HasForeignKey(team => team.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TeamInvitation>(invitation =>
            {
                invitation.ToTable("TeamInvitations");
                invitation.HasKey(value => value.Id);
                invitation.Property(value => value.Id).ValueGeneratedNever();
                invitation.Property(value => value.Email).HasMaxLength(256).IsRequired();
                invitation.Property(value => value.NormalizedEmail).HasMaxLength(256).IsRequired();
                invitation.Property(value => value.TokenHash).HasMaxLength(64).IsRequired();
                invitation.Property(value => value.Version).IsConcurrencyToken();
                invitation.HasIndex(value => value.TokenHash).IsUnique();
                invitation.HasIndex(value => new { value.TeamId, value.CreatedAtUtc });
                invitation.HasOne<Team>().WithMany().HasForeignKey(value => value.TeamId).OnDelete(DeleteBehavior.Cascade);
                invitation.HasOne<User>().WithMany().HasForeignKey(value => value.IssuedByUserId).OnDelete(DeleteBehavior.Restrict);
                invitation.HasOne<User>().WithMany().HasForeignKey(value => value.AcceptedByUserId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<User>()
                .HasOne(u => u.Team)
                .WithMany(t => t.Users)
                .HasForeignKey(u => u.TeamId);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Tasks)
                .WithOne(t => t.Performer)
                .HasForeignKey(t => t.PerformerId);

            modelBuilder.Entity<User>()
                .Property(u => u.TeamId)
                .IsRequired(false);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // A scan must fail if another scan or an API update changes the
            // occurrence while it is creating or completing an outbox entry.
            modelBuilder.Entity<Task>().Property(t => t.Version).IsConcurrencyToken().ValueGeneratedNever();
            modelBuilder.Entity<Task>().HasIndex(t => new { t.ProjectId, t.CreatedAt, t.Id });
            modelBuilder.Entity<Task>().HasIndex(t => new { t.PerformerId, t.DueDate, t.Id });
            modelBuilder.Entity<Task>().Property(t => t.DueDate).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.State).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.PerformerId).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.ReminderEnabled).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.ReminderOffsetMinutes).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.ReminderSentAt).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.ReminderNotificationId).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.EscalationEnabled).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.EscalationDelayMinutes).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.EscalationSentAt).IsConcurrencyToken();
            modelBuilder.Entity<Task>().Property(t => t.EscalationNotificationId).IsConcurrencyToken();

            modelBuilder.Entity<NotificationOutboxMessage>(outbox =>
            {
                outbox.ToTable("NotificationOutbox");
                outbox.HasKey(m => m.Id);
                outbox.Property(m => m.Id).ValueGeneratedNever();
                outbox.Property(m => m.QueueName).HasMaxLength(256).IsRequired();
                outbox.Property(m => m.Payload).IsRequired();
                outbox.Property(m => m.LockId).IsConcurrencyToken();
                outbox.Property(m => m.Status).IsConcurrencyToken();
                outbox.HasIndex(m => new { m.Status, m.LockedUntilUtc, m.CreatedAtUtc });
                outbox.HasOne<Task>().WithMany().HasForeignKey(m => m.TaskId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ExecutedTask>().HasKey(e => e.Id);
            modelBuilder.ConfigureAiDrafts();
            modelBuilder.ConfigureNotificationInbox();
        }

    }
}
