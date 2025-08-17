using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace DAL.Entities;

public class User : IdentityUser<int>
{
    public int? TeamId { get; set; }

    [MaxLength(100)]
    public string FirstName { get; set; }
    [MaxLength(100)]
    public string LastName { get; set; }

    public DateTime RegisteredAt { get; set; }
    public DateTime BirthDay { get; set; }

    public Team? Team { get; set; }

    public List<Task>? Tasks { get; set; }

    public User(int id, int? teamId, string userName, string firstName, string lastName, string email, DateTime registeredAt, DateTime birthDay, Team team, List<Task> tasks)
    {
        Id = id;
        TeamId = teamId;
        UserName = userName;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        RegisteredAt = registeredAt;
        BirthDay = birthDay;
        Team = team;
        Tasks = tasks;
    }

    public User()
    {

    }

    public User(string userName, string firstName, string lastName, string email) : base(userName)
    {
        UserName = userName;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
    }
}
