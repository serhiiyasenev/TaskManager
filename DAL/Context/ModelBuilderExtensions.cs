using DAL.Entities;
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

            modelBuilder.Entity<Task>()
                .HasOne(t => t.Performer)
                .WithMany()
                .HasForeignKey(t => t.PerformerId);

            modelBuilder.Entity<Task>()
                .HasOne(t => t.Project)
                .WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId);

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

            modelBuilder.Entity<ExecutedTask>().HasKey(e => e.Id);
        }

        public static void Seed(this ModelBuilder modelBuilder)
        {
            var t0 = new DateTime(2025, 08, 01, 00, 00, 00, DateTimeKind.Utc);

            modelBuilder.Entity<Team>().HasData(
                new Team { Id = 1, Name = "Team 1", CreatedAt = t0.AddDays(-3) },
                new Team { Id = 2, Name = "Team 2", CreatedAt = t0.AddDays(-3) },
                new Team { Id = 3, Name = "Team 3", CreatedAt = t0.AddDays(-3) }
            );

            modelBuilder.Entity<User>().HasData(
                new User { Id = 1, FirstName = "John", LastName = "A", TeamId = 1, Email = "john.A.doe@gmail.com", RegisteredAt = t0.AddDays(-3), BirthDay = new DateTime(1991, 01, 01) },
                new User { Id = 2, FirstName = "John", LastName = "B", TeamId = 2, Email = "john.B.doe@gmail.com", RegisteredAt = t0.AddDays(-3), BirthDay = new DateTime(1992, 02, 02) },
                new User { Id = 3, FirstName = "John", LastName = "C", TeamId = 3, Email = "john.C.doe@gmail.com", RegisteredAt = t0.AddDays(-3), BirthDay = new DateTime(1993, 03, 03) },
                new User { Id = 4, FirstName = "John", LastName = "D", TeamId = 1, Email = "john.D.doe@gmail.com", RegisteredAt = t0.AddDays(-3), BirthDay = new DateTime(1994, 04, 04) },
                new User { Id = 5, FirstName = "John", LastName = "E", TeamId = 2, Email = "john.E.doe@gmail.com", RegisteredAt = t0.AddDays(-3), BirthDay = new DateTime(1995, 05, 05) }
            );

            modelBuilder.Entity<Project>().HasData(
                new Project { Id = 1, AuthorId = 1, TeamId = 1, Name = "Project 1", Description = "Description 1", CreatedAt = t0, Deadline = t0.AddDays(10) },
                new Project { Id = 2, AuthorId = 2, TeamId = 2, Name = "Project 2", Description = "Description 2", CreatedAt = t0, Deadline = t0.AddDays(15) },
                new Project { Id = 3, AuthorId = 3, TeamId = 3, Name = "Project 3", Description = "Description 3", CreatedAt = t0, Deadline = t0.AddDays(20) }
            );

            modelBuilder.Entity<Task>().HasData(
                new Task { Id = 1, Name = "Task 1", ProjectId = 1, PerformerId = 1, Description = "Task Description 1", State = TaskState.ToDo, CreatedAt = t0, FinishedAt = null },
                new Task { Id = 2, Name = "Task 2", ProjectId = 2, PerformerId = 2, Description = "Task Description 2", State = TaskState.InProgress, CreatedAt = t0, FinishedAt = null },
                new Task { Id = 3, Name = "Task 3", ProjectId = 3, PerformerId = 3, Description = "Task Description 3", State = TaskState.Done, CreatedAt = t0.AddDays(-3), FinishedAt = t0 },
                new Task { Id = 4, Name = "Task 4", ProjectId = 1, PerformerId = 1, Description = "Task Description 4", State = TaskState.ToDo, CreatedAt = t0, FinishedAt = null },
                new Task { Id = 5, Name = "Task 5", ProjectId = 2, PerformerId = 2, Description = "Task Description 5", State = TaskState.Canceled, CreatedAt = t0.AddDays(-3), FinishedAt = t0 }
            );
        }

    }
}
