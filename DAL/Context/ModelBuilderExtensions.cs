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

            modelBuilder.Entity<ExecutedTask>().HasKey(e => e.Id);
        }

        public static void Seed(this ModelBuilder modelBuilder)
        {
            string Normalize(string s) => s.ToUpperInvariant();
            var t0 = new DateTime(2025, 08, 01, 00, 00, 00, DateTimeKind.Utc);
            const string pwdHash = "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==";

            modelBuilder.Entity<Team>().HasData(
                new Team { Id = 1, Name = "Team 1", CreatedAt = t0.AddDays(-3) },
                new Team { Id = 2, Name = "Team 2", CreatedAt = t0.AddDays(-3) },
                new Team { Id = 3, Name = "Team 3", CreatedAt = t0.AddDays(-3) },
                new Team { Id = 4, Name = "Team 4", CreatedAt = t0.AddDays(-6) },
                new Team { Id = 5, Name = "Team 5", CreatedAt = t0.AddDays(-6) }
            );

            modelBuilder.Entity<User>().HasData(
                new User
                {
                    Id = 1,
                    TeamId = 1,
                    UserName = "john.a",
                    NormalizedUserName = Normalize("john.a"),
                    Email = "john.A.doe@gmail.com",
                    NormalizedEmail = Normalize("john.a.doe@gmail.com"),
                    FirstName = "John",
                    LastName = "A",
                    RegisteredAt = t0.AddDays(-3),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1991, 01, 01)
                },
                new User
                {
                    Id = 2,
                    TeamId = 1,
                    UserName = "kate.a",
                    NormalizedUserName = Normalize("kate.a"),
                    Email = "kate.a@example.com",
                    NormalizedEmail = Normalize("kate.a@example.com"),
                    FirstName = "Kate",
                    LastName = "A",
                    RegisteredAt = t0.AddDays(-3),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1990, 05, 10)
                },
                new User
                {
                    Id = 3,
                    TeamId = 2,
                    UserName = "john.b",
                    NormalizedUserName = Normalize("john.b"),
                    Email = "john.b.doe@gmail.com",
                    NormalizedEmail = Normalize("john.b.doe@gmail.com"),
                    FirstName = "John",
                    LastName = "B",
                    RegisteredAt = t0.AddDays(-3),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1992, 02, 02)
                },
                new User
                {
                    Id = 4,
                    TeamId = 2,
                    UserName = "kate.b",
                    NormalizedUserName = Normalize("kate.b"),
                    Email = "kate.b@example.com",
                    NormalizedEmail = Normalize("kate.b@example.com"),
                    FirstName = "Kate",
                    LastName = "B",
                    RegisteredAt = t0.AddDays(-3),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1991, 06, 15)
                },
                new User
                {
                    Id = 5,
                    TeamId = 3,
                    UserName = "john.c",
                    NormalizedUserName = Normalize("john.c"),
                    Email = "john.c.doe@gmail.com",
                    NormalizedEmail = Normalize("john.c.doe@gmail.com"),
                    FirstName = "John",
                    LastName = "C",
                    RegisteredAt = t0.AddDays(-3),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1993, 03, 03)
                },
                new User
                {
                    Id = 6,
                    TeamId = 3,
                    UserName = "kate.c",
                    NormalizedUserName = Normalize("kate.c"),
                    Email = "kate.c@example.com",
                    NormalizedEmail = Normalize("kate.c@example.com"),
                    FirstName = "Kate",
                    LastName = "C",
                    RegisteredAt = t0.AddDays(-3),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1989, 08, 20)
                },
                new User
                {
                    Id = 7,
                    TeamId = 4,
                    UserName = "john.d",
                    NormalizedUserName = Normalize("john.d"),
                    Email = "john.d.doe@gmail.com",
                    NormalizedEmail = Normalize("john.d.doe@gmail.com"),
                    FirstName = "John",
                    LastName = "D",
                    RegisteredAt = t0.AddDays(-6),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1994, 04, 04)
                },
                new User
                {
                    Id = 8,
                    TeamId = 4,
                    UserName = "kate.d",
                    NormalizedUserName = Normalize("kate.d"),
                    Email = "kate.d@example.com",
                    NormalizedEmail = Normalize("kate.d@example.com"),
                    FirstName = "Kate",
                    LastName = "D",
                    RegisteredAt = t0.AddDays(-6),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1990, 12, 02)
                },
                new User
                {
                    Id = 9,
                    TeamId = 5,
                    UserName = "john.e",
                    NormalizedUserName = Normalize("john.e"),
                    Email = "john.e.doe@gmail.com",
                    NormalizedEmail = Normalize("john.e.doe@gmail.com"),
                    FirstName = "John",
                    LastName = "E",
                    RegisteredAt = t0.AddDays(-6),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1995, 05, 05)
                },
                new User
                {
                    Id = 10,
                    TeamId = 5,
                    UserName = "kate.e",
                    NormalizedUserName = Normalize("kate.e"),
                    Email = "kate.e@example.com",
                    NormalizedEmail = Normalize("kate.e@example.com"),
                    FirstName = "Kate",
                    LastName = "E",
                    RegisteredAt = t0.AddDays(-6),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.UserSecurityStamp,
                    ConcurrencyStamp = SeedConstants.UserConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1988, 07, 07)
                },
                new User
                {
                    Id = 11,
                    TeamId = null,
                    UserName = "admin",
                    NormalizedUserName = Normalize("admin"),
                    Email = "admin@example.com",
                    NormalizedEmail = Normalize("admin@example.com"),
                    FirstName = "System",
                    LastName = "Admin",
                    RegisteredAt = t0.AddDays(-10),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.AdminSecurityStamp,
                    ConcurrencyStamp = SeedConstants.AdminConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(1985, 01, 01),
                },
                new User
                {
                    Id = 12,
                    TeamId = null,
                    UserName = "service.bot",
                    NormalizedUserName = Normalize("service.bot"),
                    Email = "service.bot@example.com",
                    NormalizedEmail = Normalize("service.bot@example.com"),
                    FirstName = "Service",
                    LastName = "Bot",
                    RegisteredAt = t0.AddDays(-10),
                    EmailConfirmed = true,
                    PasswordHash = pwdHash,
                    SecurityStamp = SeedConstants.BotSecurityStamp,
                    ConcurrencyStamp = SeedConstants.BotConcurrencyStamp,
                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    LockoutEnabled = false,
                    AccessFailedCount = 0,
                    BirthDay = new DateTime(2000, 01, 01)
                }
            );

            modelBuilder.Entity<IdentityRole<int>>().HasData(
                new IdentityRole<int>
                {
                    Id = 1,
                    Name = "admin",
                    NormalizedName = "ADMIN",
                    ConcurrencyStamp = SeedConstants.RoleAdminConcurrencyStamp
                }
            );

            modelBuilder.Entity<IdentityUserRole<int>>().HasData(
                new IdentityUserRole<int>
                {
                    UserId = 11,
                    RoleId = 1
                }
            );

            modelBuilder.Entity<Project>().HasData(
                new Project { Id = 1, AuthorId = 1, TeamId = 1, Name = "Task Manager API", Description = "Core Web API", CreatedAt = t0, Deadline = t0.AddDays(20) },
                new Project { Id = 2, AuthorId = 3, TeamId = 2, Name = "Realtime Hub", Description = "SignalR hubs & clients", CreatedAt = t0.AddDays(1), Deadline = t0.AddDays(25) },
                new Project { Id = 3, AuthorId = 5, TeamId = 3, Name = "Broker Layer", Description = "RabbitMQ publishers/consumers", CreatedAt = t0.AddDays(2), Deadline = t0.AddDays(30) },
                new Project { Id = 4, AuthorId = 7, TeamId = 4, Name = "Auth & Identity", Description = "JWT, refresh tokens", CreatedAt = t0.AddDays(-2), Deadline = t0.AddDays(15) },
                new Project { Id = 5, AuthorId = 9, TeamId = 5, Name = "Observability", Description = "OTel, tracing, metrics", CreatedAt = t0.AddDays(-1), Deadline = t0.AddDays(18) },
                new Project { Id = 6, AuthorId = 2, TeamId = 1, Name = "Admin Panel", Description = "Backoffice UI", CreatedAt = t0.AddDays(-3), Deadline = t0.AddDays(22) }
            );

            modelBuilder.Entity<Task>().HasData(
                // Project 1 (Task Manager API)
                new Task { Id = 1, ProjectId = 1, PerformerId = 1, Name = "Design DB schema", Description = "Initial ERD + migrations", State = TaskState.Done, CreatedAt = t0.AddDays(-3), FinishedAt = t0 },
                new Task { Id = 2, ProjectId = 1, PerformerId = 2, Name = "Implement DAL", Description = "EF Core, repositories", State = TaskState.InProgress, CreatedAt = t0.AddDays(-1), FinishedAt = null },
                new Task { Id = 3, ProjectId = 1, PerformerId = 2, Name = "Seed data", Description = "Teams/Users/Projects/Tasks", State = TaskState.ToDo, CreatedAt = t0, FinishedAt = null },

                // Project 2 (Realtime Hub)
                new Task { Id = 4, ProjectId = 2, PerformerId = 3, Name = "Create SignalR hub", Description = "User + project groups", State = TaskState.InProgress, CreatedAt = t0.AddDays(-2), FinishedAt = null },
                new Task { Id = 5, ProjectId = 2, PerformerId = 4, Name = "Auth for hubs", Description = "JWT bearer auth", State = TaskState.ToDo, CreatedAt = t0, FinishedAt = null },
                new Task { Id = 6, ProjectId = 2, PerformerId = 4, Name = "Backplane setup", Description = "Redis backplane", State = TaskState.ToDo, CreatedAt = t0.AddDays(1), FinishedAt = null },

                // Project 3 (Broker Layer)
                new Task { Id = 7, ProjectId = 3, PerformerId = 5, Name = "Publishers", Description = "Outbox pattern", State = TaskState.ToDo, CreatedAt = t0.AddDays(-1), FinishedAt = null },
                new Task { Id = 8, ProjectId = 3, PerformerId = 6, Name = "Consumers", Description = "Idempotency, DLQ", State = TaskState.ToDo, CreatedAt = t0, FinishedAt = null },
                new Task { Id = 9, ProjectId = 3, PerformerId = 6, Name = "Retry policy", Description = "Exponential backoff", State = TaskState.ToDo, CreatedAt = t0.AddDays(1), FinishedAt = null },

                // Project 4 (Auth & Identity)
                new Task { Id = 10, ProjectId = 4, PerformerId = 7, Name = "JWT endpoints", Description = "Login/refresh/roles", State = TaskState.InProgress, CreatedAt = t0.AddDays(-2), FinishedAt = null },
                new Task { Id = 11, ProjectId = 4, PerformerId = 8, Name = "Password policies", Description = "Strong defaults", State = TaskState.ToDo, CreatedAt = t0, FinishedAt = null },
                new Task { Id = 12, ProjectId = 4, PerformerId = 8, Name = "Seed admin role", Description = "RBAC", State = TaskState.ToDo, CreatedAt = t0.AddDays(1), FinishedAt = null },

                // Project 5 (Observability)
                new Task { Id = 13, ProjectId = 5, PerformerId = 9, Name = "OTel tracing", Description = "ActivitySource", State = TaskState.ToDo, CreatedAt = t0.AddDays(-1), FinishedAt = null },
                new Task { Id = 14, ProjectId = 5, PerformerId = 10, Name = "Metrics exporter", Description = "Prometheus", State = TaskState.ToDo, CreatedAt = t0, FinishedAt = null },
                new Task { Id = 15, ProjectId = 5, PerformerId = 10, Name = "Logs pipeline", Description = "Serilog + sinks", State = TaskState.ToDo, CreatedAt = t0.AddDays(2), FinishedAt = null },

                // Project 6 (Admin Panel)
                new Task { Id = 16, ProjectId = 6, PerformerId = 11, Name = "Scaffold admin UI", Description = "Users/Teams grid", State = TaskState.InProgress, CreatedAt = t0.AddDays(-3), FinishedAt = null },
                new Task { Id = 17, ProjectId = 6, PerformerId = 12, Name = "RBAC in UI", Description = "Guarded routes", State = TaskState.ToDo, CreatedAt = t0, FinishedAt = null },
                new Task { Id = 18, ProjectId = 6, PerformerId = 12, Name = "Audit logs view", Description = "Filters & export", State = TaskState.ToDo, CreatedAt = t0.AddDays(1), FinishedAt = null }
            );

            modelBuilder.Entity<ExecutedTask>().HasData(
                new ExecutedTask { Id = 1, TaskId = 1, TaskName = "Design DB schema", CreatedAt = t0.AddDays(-3) },
                new ExecutedTask { Id = 2, TaskId = 2, TaskName = "Implement DAL", CreatedAt = t0.AddDays(-1) },
                new ExecutedTask { Id = 3, TaskId = 3, TaskName = "Seed data", CreatedAt = t0.AddDays(-2) },
                new ExecutedTask { Id = 4, TaskId = 10, TaskName = "JWT endpoints", CreatedAt = t0.AddDays(-2) },
                new ExecutedTask { Id = 5, TaskId = 11, TaskName = "Password policies", CreatedAt = t0.AddDays(-3) },
                new ExecutedTask { Id = 6, TaskId = 12, TaskName = "Publishers", CreatedAt = t0.AddDays(-1) },
                new ExecutedTask { Id = 7, TaskId = 13, TaskName = "OTel tracing", CreatedAt = t0.AddDays(-1) },
                new ExecutedTask { Id = 8, TaskId = 14, TaskName = "Metrics exporter", CreatedAt = t0 }
            );
        }

        public static class SeedConstants
        {
            public const string AdminSecurityStamp = "11111111-1111-1111-1111-111111111111";
            public const string AdminConcurrencyStamp = "22222222-2222-2222-2222-222222222222";

            public const string BotSecurityStamp = "33333333-3333-3333-3333-333333333333";
            public const string BotConcurrencyStamp = "44444444-4444-4444-4444-444444444444";

            public const string UserSecurityStamp = "55555555-5555-5555-5555-555555555555";
            public const string UserConcurrencyStamp = "66666666-6666-6666-6666-666666666666";

            public const string RoleAdminConcurrencyStamp = "77777777-7777-7777-7777-777777777777";
        }
    }
}
