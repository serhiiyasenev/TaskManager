using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Task = DAL.Entities.Task;

namespace DAL.Context
{
    public class TaskContext(DbContextOptions<TaskContext> options) : DbContext(options)
    {
        public DbSet<Project> Projects { get; private set; }
        public DbSet<Task> Tasks { get; private set; }
        public DbSet<Team> Teams { get; private set; }
        public DbSet<User> Users { get; private set; }
        public DbSet<ExecutedTask> ExecutedTasks { get; private set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Configure();
            modelBuilder.Seed();
        }
    }
}
