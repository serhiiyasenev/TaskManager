using BLL.Common;
using BLL.Interfaces;
using BLL.Interfaces.Analytics;
using BLL.Models;
using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using Xunit;
using ExecutedTaskEntity = DAL.Entities.ExecutedTask;
using ProjectEntity = DAL.Entities.Project;
using TaskEntity = DAL.Entities.Task;
using TeamEntity = DAL.Entities.Team;

namespace Tests.Unit;

public class WebApiControllersTests
{
    [Fact]
    public async Task ProjectsController_AllEndpoints_ReturnOk()
    {
        var service = new Mock<IProjectsService>();
        service.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ProjectDetailDto>>.Success([]));
        service.Setup(x => x.GetProjectByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectDetailDto>.Success(default!));
        service.Setup(x => x.AddProjectAsync(It.IsAny<ProjectEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectEntity>.Success(new ProjectEntity()));
        service.Setup(x => x.UpdateProjectByIdAsync(1, It.IsAny<ProjectEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectEntity>.Success(new ProjectEntity()));
        service.Setup(x => x.DeleteProjectByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var controller = new ProjectsController(service.Object);

        Assert.IsType<OkObjectResult>((await controller.GetAll(CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetById(1, CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Add(new ProjectEntity(), CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Update(1, new ProjectEntity(), CancellationToken.None)).Result);
        Assert.IsType<OkResult>(await controller.DeleteById(1, CancellationToken.None));
    }

    [Fact]
    public async Task TeamsController_AllEndpoints_ReturnOk()
    {
        var service = new Mock<ITeamsService>();
        service.Setup(x => x.GetTeamsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<TeamDetailDto>>.Success([]));
        service.Setup(x => x.GetTeamByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TeamDetailDto>.Success(default!));
        service.Setup(x => x.AddTeamAsync(It.IsAny<CreateTeamDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TeamEntity>.Success(new TeamEntity()));
        service.Setup(x => x.UpdateTeamByIdAsync(1, It.IsAny<UpdateTeamDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TeamEntity>.Success(new TeamEntity()));
        service.Setup(x => x.DeleteTeamByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var controller = new TeamsController(service.Object);

        Assert.IsType<OkObjectResult>((await controller.GetAll(CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetById(1, CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Add(new CreateTeamDto("t"), CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Update(1, new UpdateTeamDto("t2"), CancellationToken.None)).Result);
        Assert.IsType<OkResult>(await controller.DeleteById(1, CancellationToken.None));
    }

    [Fact]
    public async Task UsersController_AllEndpoints_ReturnOk()
    {
        var usersService = new Mock<IUsersService>();
        var authService = new Mock<IAuthService>();

        authService.Setup(x => x.RegisterAsync(It.IsAny<RegisterUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");
        authService.Setup(x => x.LoginAsync(It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserLoginInfoDto { Token = "jwt" });

        usersService.Setup(x => x.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<UserDetailDto>>.Success([]));
        usersService.Setup(x => x.GetUserByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDetailDto>.Success(default!));
        usersService.Setup(x => x.UpdateUserByIdAsync(1, It.IsAny<UpdateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<UserDto>.Success(new UserDto(1, null, "f", "l", "a@a.com", DateTime.UtcNow, DateTime.UtcNow)));
        usersService.Setup(x => x.DeleteUserByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var controller = new UsersController(usersService.Object, authService.Object);

        Assert.IsType<OkObjectResult>(await controller.Register(new RegisterUserDto("u", "f", "l", "u@e.com", "Password1!")));
        Assert.IsType<OkObjectResult>(await controller.Login(new LoginUserDto("u@e.com", "Password1!")));
        Assert.IsType<OkObjectResult>((await controller.GetAll(CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetById(1, CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Update(1, new UpdateUserDto(), CancellationToken.None)).Result);
        Assert.IsType<OkResult>(await controller.DeleteById(1, CancellationToken.None));
    }

    [Fact]
    public async Task TasksController_AllEndpoints_ReturnExpectedResults()
    {
        var tasksService = new Mock<ITasksService>();
        var queueService = new Mock<IQueueService>();

        tasksService.Setup(x => x.GetTasksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<TaskDetailDto>>.Success([]));
        tasksService.Setup(x => x.GetTaskByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskDetailDto>.Success(default!));
        tasksService.Setup(x => x.AddTaskAsync(It.IsAny<TaskEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskEntity>.Success(new TaskEntity()));
        tasksService.Setup(x => x.UpdateTaskByIdAsync(1, It.IsAny<TaskEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskEntity>.Success(new TaskEntity()));
        tasksService.Setup(x => x.DeleteTaskByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var executed = new ExecutedTaskEntity { Id = 7, TaskId = 4, TaskName = "x", CreatedAt = DateTime.UtcNow };
        tasksService.Setup(x => x.AddExecutedTaskAsync(It.IsAny<ExecutedTaskEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExecutedTaskEntity>.Success(executed));
        queueService.Setup(x => x.PostValue(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new TasksController(tasksService.Object, queueService.Object);

        Assert.IsType<OkObjectResult>((await controller.GetAll(CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetById(1, CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Add(new TaskEntity(), CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Update(1, new TaskEntity(), CancellationToken.None)).Result);
        Assert.IsType<OkResult>(await controller.DeleteById(1, CancellationToken.None));

        var ok = Assert.IsType<OkObjectResult>((await controller.AddExecutedTask(new ExecutedTaskEntity(), CancellationToken.None)).Result);
        var payload = Assert.IsType<ExecutedTaskResult>(ok.Value);
        Assert.True(payload.PostedMessageToQueueResult);
    }

    [Fact]
    public async Task TasksController_AddExecutedTask_Failure_Returns500()
    {
        var tasksService = new Mock<ITasksService>();
        var queueService = new Mock<IQueueService>();

        tasksService.Setup(x => x.AddExecutedTaskAsync(It.IsAny<ExecutedTaskEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExecutedTaskEntity>.Failure(Error.UnexpectedError));

        var controller = new TasksController(tasksService.Object, queueService.Object);

        var result = await controller.AddExecutedTask(new ExecutedTaskEntity(), CancellationToken.None);
        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task AnalyticsController_AllEndpoints_ReturnOk()
    {
        var taskAnalytics = new Mock<ITaskAnalyticsService>();
        var projectAnalytics = new Mock<IProjectAnalyticsService>();
        var teamAnalytics = new Mock<ITeamAnalyticsService>();
        var userAnalytics = new Mock<IUserAnalyticsService>();

        taskAnalytics.Setup(x => x.GetCapitalTasksByUserIdAsync(1)).ReturnsAsync([]);
        taskAnalytics.Setup(x => x.GetTasksCountInProjectsByUserIdAsync(1)).ReturnsAsync(new Dictionary<string, int>());
        taskAnalytics.Setup(x => x.GetTaskStatusByProjectAsync(2)).ReturnsAsync([]);

        projectAnalytics.Setup(x => x.GetProjectsByTeamSizeAsync(3))
            .ReturnsAsync([(1, "P", 3)]);
        projectAnalytics.Setup(x => x.GetProjectsInfoAsync()).ReturnsAsync([]);
        projectAnalytics.Setup(x => x.GetSortedFilteredPageOfProjectsAsync(
                It.IsAny<PageModel>(),
                It.IsAny<FilterModel>(),
                It.IsAny<SortingModel>()))
            .ReturnsAsync(new PagedList<FullProjectDto>([], 0));

        teamAnalytics.Setup(x => x.GetSortedTeamByMembersWithYearAsync(2020)).ReturnsAsync([]);
        userAnalytics.Setup(x => x.GetSortedUsersWithSortedTasksAsync()).ReturnsAsync([]);
        userAnalytics.Setup(x => x.GetUserInfoAsync(1)).ReturnsAsync(default(UserInfoDto)!);

        var controller = new AnalyticsController(taskAnalytics.Object, projectAnalytics.Object, teamAnalytics.Object, userAnalytics.Object);

        Assert.IsType<OkObjectResult>((await controller.GetCapitalTasksByUserIdAsync(1)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetProjectsByTeamSizeAsync(3)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetProjectsInfoAsync()).Result);
        Assert.IsType<OkObjectResult>((await controller.GetSortedFilteredPageOfProjectsAsync(
            new PageModel(10, 1), new FilterModel("n"), new SortingModel(SortingProperty.Name, SortingOrder.Ascending))).Result);
        Assert.IsType<OkObjectResult>((await controller.GetSortedTeamByMembersWithYearAsync(2020)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetSortedUsersWithSortedTasksAsync()).Result);
        Assert.IsType<OkObjectResult>((await controller.GetTasksCountInProjectsByUserIdAsync(1)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetUserInfoAsync(1)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetTaskStatusByProjectAsync(2)).Result);
    }

    [Fact]
    public async Task AnalyticsController_GetSortedFilteredPageOfProjects_NormalizesInvalidParameters()
    {
        var taskAnalytics = new Mock<ITaskAnalyticsService>();
        var projectAnalytics = new Mock<IProjectAnalyticsService>();
        var teamAnalytics = new Mock<ITeamAnalyticsService>();
        var userAnalytics = new Mock<IUserAnalyticsService>();

        projectAnalytics.Setup(x => x.GetSortedFilteredPageOfProjectsAsync(
                It.Is<PageModel>(p => p.PageSize == 20 && p.PageNumber == 1),
                It.Is<FilterModel?>(f => f == null),
                It.IsAny<SortingModel>()))
            .ReturnsAsync(new PagedList<FullProjectDto>([], 0));

        var controller = new AnalyticsController(taskAnalytics.Object, projectAnalytics.Object, teamAnalytics.Object, userAnalytics.Object);

        var result = await controller.GetSortedFilteredPageOfProjectsAsync(
            new PageModel(0, 0),
            new FilterModel(),
            new SortingModel(SortingProperty.Name, SortingOrder.Descending));

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
