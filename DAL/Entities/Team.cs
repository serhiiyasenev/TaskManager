using System.ComponentModel.DataAnnotations;
using DAL.Entities.Base;
using Newtonsoft.Json;

namespace DAL.Entities;

public class Team : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public List<User> Users { get; set; }
    [JsonIgnore]
    public List<Project> Projects { get; set; }

    public Team(int id, string name, DateTime createdAt, List<User> users, List<Project> projects)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
        Users = users;
        Projects = projects;
    }

    public Team()
    {

    }
}
