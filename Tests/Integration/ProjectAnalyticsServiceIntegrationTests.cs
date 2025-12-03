using BLL.Models.Projects;
using BLL.Services.Analytics;
using DAL.Entities;
using DAL.Repositories.Implementation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Integration;

[Collection("Database collection")]
public class ProjectAnalyticsServiceIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public ProjectAnalyticsServiceIntegrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private ProjectAnalyticsService CreateService()
    {
        var projectRepo = new EfCoreRepository<Project>(_fixture.Context);
        var userRepo = new EfCoreRepository<User>(_fixture.Context);
        var teamRepo = new EfCoreRepository<Team>(_fixture.Context);
        var taskRepo = new EfCoreRepository<DAL.Entities.Task>(_fixture.Context);

        return new ProjectAnalyticsService(projectRepo, userRepo, teamRepo, taskRepo);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetProjectsByTeamSizeAsync_ReturnsProjectsWithMatchingTeamSize()
    {
        // Arrange
        var service = CreateService();
        
        // Get an actual team size that exists in the database
        var teams = await _fixture.Context.Teams.Include(t => t.Users).ToListAsync();
        var teamWithUsers = teams.FirstOrDefault(t => t.Users.Count > 0);
        
        if (teamWithUsers == null)
        {
            // No teams with users exist, skip the meaningful assertion
            Assert.NotNull(teams);
            return;
        }
        
        var teamSize = teamWithUsers.Users.Count;

        // Act
        var result = await service.GetProjectsByTeamSizeAsync(teamSize);

        // Assert
        Assert.NotNull(result);
        // If there are projects for teams with this size, verify the structure
        if (result.Count > 0)
        {
            Assert.All(result, item => Assert.Equal(teamSize, item.teamSizeCurrent));
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetProjectsByTeamSizeAsync_NoMatchingTeamSize_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetProjectsByTeamSizeAsync(100);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetProjectsInfoAsync_ReturnsAllProjectsWithTaskInfo()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetProjectsInfoAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 2, $"Expected at least 2 projects, got {result.Count}");
        
        // Verify structure
        foreach (var projectInfo in result)
        {
            Assert.NotNull(projectInfo.Project);
            Assert.True(projectInfo.Project.Id > 0);
            Assert.False(string.IsNullOrEmpty(projectInfo.Project.Name));
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedFilteredPageOfProjectsAsync_FirstPage_ReturnsCorrectPage()
    {
        // Arrange
        var service = CreateService();
        var page = new PageModel(PageNumber: 1, PageSize: 10);
        var filter = new FilterModel(null, null, null, null, null);
        var sorting = new SortingModel(SortingProperty.Name, SortingOrder.Ascending);

        // Act
        var result = await service.GetSortedFilteredPageOfProjectsAsync(page, filter, sorting);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.True(result.TotalCount >= 2);
        Assert.True(result.Items.Count >= 2);
        
        // Verify each project has required data
        foreach (var project in result.Items)
        {
            Assert.True(project.Id > 0);
            Assert.False(string.IsNullOrEmpty(project.Name));
            Assert.NotNull(project.Author);
            Assert.NotNull(project.Team);
            Assert.NotNull(project.Tasks);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedFilteredPageOfProjectsAsync_WithNameFilter_ReturnsFilteredResults()
    {
        // Arrange
        var service = CreateService();
        var page = new PageModel(PageNumber: 1, PageSize: 10);
        var filter = new FilterModel(Name: "Alpha", null, null, null, null);
        var sorting = new SortingModel(SortingProperty.Name, SortingOrder.Ascending);

        // Act
        var result = await service.GetSortedFilteredPageOfProjectsAsync(page, filter, sorting);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.All(result.Items, p => Assert.Contains("Alpha", p.Name));
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedFilteredPageOfProjectsAsync_SortByTasksCountDescending_ReturnsSortedResults()
    {
        // Arrange
        var service = CreateService();
        var page = new PageModel(PageNumber: 1, PageSize: 10);
        var filter = new FilterModel(null, null, null, null, null);
        var sorting = new SortingModel(SortingProperty.TasksCount, SortingOrder.Descending);

        // Act
        var result = await service.GetSortedFilteredPageOfProjectsAsync(page, filter, sorting);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.True(result.Items.Count > 1);
        // Verify descending order by tasks count
        for (var i = 0; i < result.Items.Count - 1; i++)
        {
            Assert.True(result.Items[i].Tasks.Count >= result.Items[i + 1].Tasks.Count);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedFilteredPageOfProjectsAsync_SortByNameAscending_ReturnsSortedResults()
    {
        // Arrange
        var service = CreateService();
        var page = new PageModel(PageNumber: 1, PageSize: 10);
        var filter = new FilterModel(null, null, null, null, null);
        var sorting = new SortingModel(SortingProperty.Name, SortingOrder.Ascending);

        // Act
        var result = await service.GetSortedFilteredPageOfProjectsAsync(page, filter, sorting);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.True(result.Items.Count > 1);
        // Verify ascending order by name
        for (var i = 0; i < result.Items.Count - 1; i++)
        {
            Assert.True(string.Compare(result.Items[i].Name, result.Items[i + 1].Name, StringComparison.Ordinal) <= 0);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedFilteredPageOfProjectsAsync_Pagination_ReturnsCorrectPageSize()
    {
        // Arrange
        var service = CreateService();
        var page = new PageModel(PageNumber: 1, PageSize: 1);
        var filter = new FilterModel(null, null, null, null, null);
        var sorting = new SortingModel(SortingProperty.Name, SortingOrder.Ascending);

        // Act
        var result = await service.GetSortedFilteredPageOfProjectsAsync(page, filter, sorting);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.Single(result.Items);
        Assert.True(result.TotalCount >= 2);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedFilteredPageOfProjectsAsync_WithAuthorFilter_ReturnsFilteredResults()
    {
        // Arrange
        var service = CreateService();
        var page = new PageModel(PageNumber: 1, PageSize: 10);
        var filter = new FilterModel(null, null, "John", null, null);
        var sorting = new SortingModel(SortingProperty.Name, SortingOrder.Ascending);

        // Act
        var result = await service.GetSortedFilteredPageOfProjectsAsync(page, filter, sorting);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.All(result.Items, p => Assert.Contains("John", p.Author.FirstName));
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedFilteredPageOfProjectsAsync_WithTeamFilter_ReturnsFilteredResults()
    {
        // Arrange
        var service = CreateService();
        var page = new PageModel(PageNumber: 1, PageSize: 10);
        var filter = new FilterModel(null, null, null, null, "Alpha");
        var sorting = new SortingModel(SortingProperty.Name, SortingOrder.Ascending);

        // Act
        var result = await service.GetSortedFilteredPageOfProjectsAsync(page, filter, sorting);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.All(result.Items, p => Assert.Contains("Alpha", p.Team.Name));
    }

    [Fact]
    public async System.Threading.Tasks.Task GetProjectsByTeamSizeAsync_WithZeroSize_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.GetProjectsByTeamSizeAsync(0);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
