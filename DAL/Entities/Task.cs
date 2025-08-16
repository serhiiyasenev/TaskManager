using System.ComponentModel.DataAnnotations;
using DAL.Entities.Base;
using DAL.Enum;
using Newtonsoft.Json;

namespace DAL.Entities;

public class Task : BaseEntity
{
    public int ProjectId { get; set; }
    public int PerformerId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; }
    [MaxLength(1000)]
    public string Description { get; set; }
    public TaskState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    [JsonIgnore]
    public Project Project { get; set; }
    [JsonIgnore]
    public User Performer { get; set; }


    public Task(int id, int projectId, int performerId, string name, string description, TaskState state, DateTime createdAt, DateTime? finishedAt, Project project, User user, User performer)
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

    public Task()
    {

    }
}
