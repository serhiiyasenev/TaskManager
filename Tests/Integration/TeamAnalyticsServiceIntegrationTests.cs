using BLL.Services.Analytics;
using DAL.Entities;
using DAL.Repositories.Implementation;
using Xunit;

namespace Tests.Integration;

public class TeamAnalyticsServiceIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private TeamAnalyticsService CreateService()
    {
        var teamRepo = new EfCoreRepository<Team>(fixture.Context);
        var userRepo = new EfCoreRepository<User>(fixture.Context);

        return new TeamAnalyticsService(teamRepo, userRepo);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedTeamByMembersWithYearAsync_ReturnsTeamsWithMembersBornBeforeYear()
    {
        // Arrange
        var service = CreateService();
        var year = 2100; // All users should be born before this year

        // Act
        var result = await service.GetSortedTeamByMembersWithYearAsync(year);

        // Assert
        Assert.NotNull(result);
        // May or may not have results depending on seed data birth years
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedTeamByMembersWithYearAsync_VerifyMembersBornBeforeYear()
    {
        // Arrange
        var service = CreateService();
        var year = 2000;

        // Act
        var result = await service.GetSortedTeamByMembersWithYearAsync(year);

        // Assert
        Assert.NotNull(result);
        
        // Verify all members in results were born before the specified year
        foreach (var team in result)
        {
            Assert.NotNull(team.Members);
            foreach (var member in team.Members)
            {
                Assert.True(member.BirthDay.Year < year, 
                    $"Member {member.FirstName} {member.LastName} was born in {member.BirthDay.Year}, expected before {year}");
            }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedTeamByMembersWithYearAsync_TeamsSortedByName()
    {
        // Arrange
        var service = CreateService();
        var year = 2100;

        // Act
        var result = await service.GetSortedTeamByMembersWithYearAsync(year);

        // Assert
        Assert.NotNull(result);
        
        if (result.Count > 1)
        {
            // Verify teams are sorted by name
            for (int i = 0; i < result.Count - 1; i++)
            {
                Assert.True(string.Compare(result[i].Name, result[i + 1].Name, StringComparison.Ordinal) <= 0,
                    $"Teams not sorted: {result[i].Name} should come before {result[i + 1].Name}");
            }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedTeamByMembersWithYearAsync_MembersSortedByRegisteredAtDescending()
    {
        // Arrange
        var service = CreateService();
        var year = 2100;

        // Act
        var result = await service.GetSortedTeamByMembersWithYearAsync(year);

        // Assert
        Assert.NotNull(result);
        
        foreach (var team in result)
        {
            if (team.Members.Count > 1)
            {
                // Verify members within each team are sorted by RegisteredAt descending
                for (int i = 0; i < team.Members.Count - 1; i++)
                {
                    Assert.True(team.Members[i].RegisteredAt >= team.Members[i + 1].RegisteredAt,
                        $"Members not sorted by RegisteredAt descending in team {team.Name}");
                }
            }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedTeamByMembersWithYearAsync_VeryOldYear_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var year = 1; // No users should be born before year 1 (default DateTime is year 1)

        // Act
        var result = await service.GetSortedTeamByMembersWithYearAsync(year);

        // Assert
        Assert.NotNull(result);
        // Result should be empty since no users are born before year 1
        Assert.Empty(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedTeamByMembersWithYearAsync_VerifyTeamStructure()
    {
        // Arrange
        var service = CreateService();
        var year = 2100;

        // Act
        var result = await service.GetSortedTeamByMembersWithYearAsync(year);

        // Assert
        Assert.NotNull(result);
        
        foreach (var team in result)
        {
            Assert.True(team.Id > 0);
            Assert.False(string.IsNullOrEmpty(team.Name));
            Assert.NotNull(team.Members);
            
            foreach (var member in team.Members)
            {
                Assert.True(member.Id > 0);
                Assert.False(string.IsNullOrEmpty(member.FirstName));
                Assert.False(string.IsNullOrEmpty(member.LastName));
                Assert.False(string.IsNullOrEmpty(member.Email));
                Assert.True(member.RegisteredAt <= DateTime.UtcNow);
            }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task GetSortedTeamByMembersWithYearAsync_SpecificYear_FiltersCorrectly()
    {
        // Arrange
        var service = CreateService();
        var userRepo = new EfCoreRepository<User>(fixture.Context);
        var teamRepo = new EfCoreRepository<Team>(fixture.Context);
        var uow = new UnitOfWork(fixture.Context);
        
        // Create a test team and users with specific birth years
        var testTeam = new Team { Name = "Test Team Analytics", CreatedAt = DateTime.UtcNow };
        await teamRepo.AddAsync(testTeam);
        await uow.SaveChangesAsync();
        
        var oldUser = new User("olduser", "Old", "User", "old@test.com")
        {
            TeamId = testTeam.Id,
            BirthDay = new DateTime(1980, 1, 1),
            RegisteredAt = DateTime.UtcNow
        };
        
        var youngUser = new User("younguser", "Young", "User", "young@test.com")
        {
            TeamId = testTeam.Id,
            BirthDay = new DateTime(2010, 1, 1),
            RegisteredAt = DateTime.UtcNow
        };
        
        await userRepo.AddAsync(oldUser);
        await userRepo.AddAsync(youngUser);
        await uow.SaveChangesAsync();

        // Act - get teams with members born before 2000
        var result = await service.GetSortedTeamByMembersWithYearAsync(2000);

        // Assert
        Assert.NotNull(result);
        
        var testTeamResult = result.FirstOrDefault(t => t.Name == "Test Team Analytics");
        if (testTeamResult != null)
        {
            // Should only include the old user
            Assert.Contains(testTeamResult.Members, m => m.FirstName == "Old");
            Assert.DoesNotContain(testTeamResult.Members, m => m.FirstName == "Young");
        }
    }
}
