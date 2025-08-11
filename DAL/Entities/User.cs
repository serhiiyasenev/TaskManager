using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace DAL.Entities;

public class User : BaseEntity
{
    public int? TeamId { get; set; }
    [MaxLength(100)]
    public string FirstName { get; set; }
    [MaxLength(100)]
    public string LastName { get; set; }
    [Required]
    public string Email { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime BirthDay { get; set; }
    [JsonIgnore]
    internal Team Team { get; set; }
    [JsonIgnore]
    internal List<Task> Tasks { get; set; }

    public User()
    {
    }

    public User(int id, int? teamId, string firstName, string lastName, string email, DateTime registeredAt, DateTime birthDay, Team team, List<Task> tasks)
    {
        Id = id;
        TeamId = teamId;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        RegisteredAt = registeredAt;
        BirthDay = birthDay;
        Team = team;
        Tasks = tasks;
    }
}
