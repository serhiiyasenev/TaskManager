using BLL.Common;
using DAL.Entities;

namespace BLL.Interfaces;

public interface IProjectsService
{
    Task<Result<List<Project>>> GetProjectsAsync(CancellationToken ct = default);
    Task<Result<Project>> GetProjectByIdAsync(int id, CancellationToken ct = default);
    Task<Result<Project>> AddProjectAsync(Project project, CancellationToken ct = default);
    Task<Result<Project>> UpdateProjectByIdAsync(int id, Project project, CancellationToken ct = default);
    Task<Result> DeleteProjectByIdAsync(int id, CancellationToken ct = default);
}
