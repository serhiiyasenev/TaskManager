using System.ComponentModel.DataAnnotations;
using DAL.Entities;

namespace BLL.Models.Projects;

public sealed class ProjectWriteRequest
{
    [Range(1, int.MaxValue)]
    public int TeamId { get; set; }
    [Required, MaxLength(100)]
    public string Name { get; set; } = "";
    [MaxLength(1000)]
    public string Description { get; set; } = "";
    public DateTime Deadline { get; set; }

    public Project ToEntity() => new() { TeamId = TeamId, Name = Name, Description = Description, Deadline = Deadline };

    public static Project Sanitize(Project input) => new()
    {
        TeamId = input.TeamId, AuthorId = input.AuthorId, Name = input.Name,
        Description = input.Description, Deadline = input.Deadline
    };
}

public sealed record ProjectWriteResponse(int Id, int AuthorId, int TeamId, string Name, string Description, DateTime CreatedAt, DateTime Deadline)
{
    public static ProjectWriteResponse FromEntity(Project project) => new(project.Id, project.AuthorId, project.TeamId,
        project.Name, project.Description, project.CreatedAt, project.Deadline);
}
