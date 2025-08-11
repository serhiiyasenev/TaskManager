using BLL.Exceptions;
using BLL.Interfaces;
using DAL.Entities;
using Task = System.Threading.Tasks.Task;

namespace BLL.Services;

public class ProjectsService(IDataProvider dataProvider) : IProjectsService
{
    public async Task<List<Project>> GetProjectsAsync()
    {
        return await dataProvider.GetProjectsAsync();
    }

    public async Task<Project> GetProjectByIdAsync(int id)
    {
        return await dataProvider.GetProjectByIdAsync(id) ?? throw new NotFoundException(nameof(Project), id);
    }

    public async Task<Project> AddProjectAsync(Project project)
    {
        return await dataProvider.AddProjectAsync(project);
    }

    public async Task<Project> UpdateProjectByIdAsync(int id, Project project)
    {
        return await dataProvider.UpdateProjectByIdAsync(id, project) ?? throw new NotFoundException(nameof(Project), id);
    }

    public async Task DeleteProjectByIdAsync(int id)
    {
        var isDeleted = await dataProvider.DeleteProjectByIdAsync(id) ?? throw new NotFoundException(nameof(Project), id);
        if (isDeleted == false) throw new CanNotDeleteException(nameof(Project), id);
    }
}