using BLL.Common;
using BLL.Models.Projects;
using DAL.Entities;

namespace BLL.Interfaces;

public interface IProjectsService
{
    Task<Result<List<ProjectDetailDto>>> GetProjectsAsync(CancellationToken ct = default);
    Task<Result<ProjectDetailDto>> GetProjectByIdAsync(int id, CancellationToken ct = default);
    Task<Result<Project>> AddProjectAsync(Project project, CancellationToken ct = default);
    Task<Result<Project>> UpdateProjectByIdAsync(int id, Project project, CancellationToken ct = default);
    Task<Result> DeleteProjectByIdAsync(int id, CancellationToken ct = default);
}
