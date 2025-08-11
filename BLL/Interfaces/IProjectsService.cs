using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Interfaces;

public interface IProjectsService
{
    Task<List<Project>> GetProjectsAsync();
    Task<Project> GetProjectByIdAsync(int id);
    Task<Project> AddProjectAsync(Project project);
    Task<Project> UpdateProjectByIdAsync(int id, Project project);
    Task DeleteProjectByIdAsync(int id);
}
