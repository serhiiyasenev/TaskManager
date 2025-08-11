using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DAL.Entities;

public class Project : BaseEntity
{
    public int AuthorId { get; set; }
    public int TeamId { get; set; }
    [Required]
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime Deadline { get; set; }
    [JsonIgnore]
    internal User Author { get; set; }
    [JsonIgnore]
    internal Team Team { get; set; }
    [JsonIgnore]
    internal List<Task> Tasks { get; set; }

    public Project()
    {
        
    }

    public Project(int id, int authorId, int teamId, string name, string description, DateTime createdAt, DateTime deadline, User author, Team team, List<Task> tasks)
    {
        Id = id;
        AuthorId = authorId;
        TeamId = teamId;
        Name = name;
        Description = description;
        CreatedAt = createdAt;
        Deadline = deadline;
        Author = author;
        Team = team;
        Tasks = tasks;
    }
}
