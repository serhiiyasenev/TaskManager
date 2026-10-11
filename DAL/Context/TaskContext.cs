using DAL.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Task = DAL.Entities.Task;

namespace DAL.Context
{
    public class TaskContext(DbContextOptions<TaskContext> options) : IdentityDbContext<User, IdentityRole<int>, int>(options)
    {
        public DbSet<ExecutedTask> ExecutedTasks { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<Task> Tasks { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<NotificationOutboxMessage> NotificationOutbox { get; set; }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            StampTaskVersions();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override System.Threading.Tasks.Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            StampTaskVersions();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void StampTaskVersions()
        {
            // Every application task write participates, including scheduler
            // occurrence changes. Original values remain the concurrency guard.
            foreach (var entry in ChangeTracker.Entries<Task>().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
                entry.Entity.Version = Guid.NewGuid();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Configure();
        }
    }
}
