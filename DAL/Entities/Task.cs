using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace DAL.Entities;

public class Task : BaseEntity
{
    public int ProjectId { get; set; }
    public int PerformerId { get; set; }
    [Required]
    public string Name { get; set; }
    public string Description { get; set; }
    public TaskState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    [JsonIgnore]
    internal Project Project { get; set; }
    [JsonIgnore]
    internal User Performer { get; set; }

    public Task()
    {
        
    }

    public Task(int id, int projectId, int performerId, string name, string description, TaskState state, DateTime createdAt, DateTime? finishedAt, Project project, User user)
    {
        Id = id;
        ProjectId = projectId;
        PerformerId = performerId;
        Name = name;
        Description = description;
        State = state;
        CreatedAt = createdAt;
        FinishedAt = finishedAt;
        Project = project;
        Performer = user;
    }
}
