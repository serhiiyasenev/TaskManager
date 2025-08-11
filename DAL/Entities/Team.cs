using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace DAL.Entities;

public class Team : BaseEntity
{
    [Required]
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    [JsonIgnore]
    internal List<User> Users { get; set; }
    [JsonIgnore]
    internal List<Project> Projects { get; set; }

    public Team()
    {
    }

    public Team(int id, string name, DateTime createdAt, List<User> users, List<Project> projects)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
        Users = users;
        Projects = projects;
    }
}
