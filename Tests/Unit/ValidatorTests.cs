using BLL.Models.Projects;
using BLL.Models.Tasks;
using BLL.Models.Teams;
using BLL.Models.Users;
using BLL.Validators;
using Xunit;

namespace Tests.Unit;

public class ValidatorTests
{
    #region RegisterUserDtoValidator Tests

    [Fact]
    public void RegisterUserDtoValidator_ValidDto_PassesValidation()
    {
        // Arrange
        var validator = new RegisterUserDtoValidator();
        var dto = new RegisterUserDto(
            UserName: "testuser",
            FirstName: "John",
            LastName: "Doe",
            Email: "test@example.com",
            Password: "StrongP@ss123"
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RegisterUserDtoValidator_EmptyUsername_FailsValidation()
    {
        // Arrange
        var validator = new RegisterUserDtoValidator();
        var dto = new RegisterUserDto(
            UserName: "",
            FirstName: "John",
            LastName: "Doe",
            Email: "test@example.com",
            Password: "StrongP@ss123"
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "UserName");
    }

    [Fact]
    public void RegisterUserDtoValidator_InvalidEmail_FailsValidation()
    {
        // Arrange
        var validator = new RegisterUserDtoValidator();
        var dto = new RegisterUserDto(
            UserName: "testuser",
            FirstName: "John",
            LastName: "Doe",
            Email: "invalid-email",
            Password: "StrongP@ss123"
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Email");
    }

    [Fact]
    public void RegisterUserDtoValidator_ShortPassword_FailsValidation()
    {
        // Arrange
        var validator = new RegisterUserDtoValidator();
        var dto = new RegisterUserDto(
            UserName: "testuser",
            FirstName: "John",
            LastName: "Doe",
            Email: "test@example.com",
            Password: "short"
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Password");
    }

    [Fact]
    public void RegisterUserDtoValidator_EmptyFirstName_FailsValidation()
    {
        // Arrange
        var validator = new RegisterUserDtoValidator();
        var dto = new RegisterUserDto(
            UserName: "testuser",
            FirstName: "",
            LastName: "Doe",
            Email: "test@example.com",
            Password: "StrongP@ss123"
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "FirstName");
    }

    [Fact]
    public void RegisterUserDtoValidator_EmptyLastName_FailsValidation()
    {
        // Arrange
        var validator = new RegisterUserDtoValidator();
        var dto = new RegisterUserDto(
            UserName: "testuser",
            FirstName: "John",
            LastName: "",
            Email: "test@example.com",
            Password: "StrongP@ss123"
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "LastName");
    }

    #endregion

    #region ProjectDtoValidator Tests

    [Fact]
    public void ProjectDtoValidator_ValidDto_PassesValidation()
    {
        // Arrange
        var validator = new ProjectDtoValidator();
        var dto = new ProjectDto(
            Id: 1,
            Name: "Test Project",
            Description: "Test Description",
            CreatedAt: DateTime.UtcNow,
            Deadline: DateTime.UtcNow.AddDays(30)
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ProjectDtoValidator_EmptyName_FailsValidation()
    {
        // Arrange
        var validator = new ProjectDtoValidator();
        var dto = new ProjectDto(
            Id: 1,
            Name: "",
            Description: "Test Description",
            CreatedAt: DateTime.UtcNow,
            Deadline: DateTime.UtcNow.AddDays(30)
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void ProjectDtoValidator_TooLongName_FailsValidation()
    {
        // Arrange
        var validator = new ProjectDtoValidator();
        var dto = new ProjectDto(
            Id: 1,
            Name: new string('A', 101), // 101 characters
            Description: "Test Description",
            CreatedAt: DateTime.UtcNow,
            Deadline: DateTime.UtcNow.AddDays(30)
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void ProjectDtoValidator_TooLongDescription_FailsValidation()
    {
        // Arrange
        var validator = new ProjectDtoValidator();
        var dto = new ProjectDto(
            Id: 1,
            Name: "Test Project",
            Description: new string('A', 1001), // 1001 characters
            CreatedAt: DateTime.UtcNow,
            Deadline: DateTime.UtcNow.AddDays(30)
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Description");
    }

    #endregion

    #region TaskDtoValidator Tests

    [Fact]
    public void TaskDtoValidator_ValidDto_PassesValidation()
    {
        // Arrange
        var validator = new TaskDtoValidator();
        var dto = new TaskDto(
            Id: 1,
            Name: "Test Task",
            Description: "Test Description",
            State: "ToDo",
            CreatedAt: DateTime.UtcNow,
            FinishedAt: null
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TaskDtoValidator_EmptyName_FailsValidation()
    {
        // Arrange
        var validator = new TaskDtoValidator();
        var dto = new TaskDto(
            Id: 1,
            Name: "",
            Description: "Test Description",
            State: "ToDo",
            CreatedAt: DateTime.UtcNow,
            FinishedAt: null
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void TaskDtoValidator_TooLongName_FailsValidation()
    {
        // Arrange
        var validator = new TaskDtoValidator();
        var dto = new TaskDto(
            Id: 1,
            Name: new string('A', 101), // 101 characters
            Description: "Test Description",
            State: "ToDo",
            CreatedAt: DateTime.UtcNow,
            FinishedAt: null
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void TaskDtoValidator_TooLongDescription_FailsValidation()
    {
        // Arrange
        var validator = new TaskDtoValidator();
        var dto = new TaskDto(
            Id: 1,
            Name: "Test Task",
            Description: new string('A', 1001), // 1001 characters
            State: "ToDo",
            CreatedAt: DateTime.UtcNow,
            FinishedAt: null
        );

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Description");
    }

    #endregion

    #region CreateTeamDtoValidator Tests

    [Fact]
    public void CreateTeamDtoValidator_ValidDto_PassesValidation()
    {
        // Arrange
        var validator = new CreateTeamDtoValidator();
        var dto = new CreateTeamDto("Test Team");

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CreateTeamDtoValidator_EmptyName_FailsValidation()
    {
        // Arrange
        var validator = new CreateTeamDtoValidator();
        var dto = new CreateTeamDto("");

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void CreateTeamDtoValidator_TooLongName_FailsValidation()
    {
        // Arrange
        var validator = new CreateTeamDtoValidator();
        var dto = new CreateTeamDto(new string('A', 101)); // 101 characters

        // Act
        var result = validator.Validate(dto);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    #endregion
}
