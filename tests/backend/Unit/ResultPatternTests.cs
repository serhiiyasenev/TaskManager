using BLL.Common;
using Xunit;

namespace Tests.Unit;

public class ResultPatternTests
{
    [Fact]
    public void Result_Success_CreatesSuccessResult()
    {
        // Arrange & Act
        var result = Result<int>.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Result_Failure_CreatesFailureResult()
    {
        // Arrange
        var error = Error.NotFound("Project", 5);

        // Act
        var result = Result<int>.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(0, result.Value);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Result_ImplicitConversionFromValue_CreatesSuccessResult()
    {
        // Arrange & Act
        Result<string> result = "Hello World";

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Hello World", result.Value);
    }

    [Fact]
    public void Result_ImplicitConversionFromError_CreatesFailureResult()
    {
        // Arrange
        var error = Error.NotFound("User", "john@example.com");

        // Act
        Result<string> result = error;

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void ResultNonGeneric_Success_CreatesSuccessResult()
    {
        // Arrange & Act
        var result = Result.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void ResultNonGeneric_Failure_CreatesFailureResult()
    {
        // Arrange
        var error = Error.ValidationFailed;

        // Act
        var result = Result.Failure(error);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Error_NotFound_CreatesCorrectError()
    {
        // Act
        var error = Error.NotFound("Project", 5);

        // Assert
        Assert.Equal("Error.NotFound", error.Code);
        Assert.Equal("Project with ID 5 was not found", error.Message);
    }

    [Fact]
    public void Error_NotFoundWithString_CreatesCorrectError()
    {
        // Act
        var error = Error.NotFound("User", "john@example.com");

        // Assert
        Assert.Equal("Error.NotFound", error.Code);
        Assert.Equal("User 'john@example.com' was not found", error.Message);
    }

    [Fact]
    public void Error_Validation_CreatesCorrectError()
    {
        // Act
        var error = Error.Validation("Email", "Invalid email format");

        // Assert
        Assert.Equal("Error.Validation.Email", error.Code);
        Assert.Equal("Invalid email format", error.Message);
    }

    [Fact]
    public void Error_Conflict_CreatesCorrectError()
    {
        // Act
        var error = Error.Conflict("Project name already exists");

        // Assert
        Assert.Equal("Error.Conflict", error.Code);
        Assert.Equal("Project name already exists", error.Message);
    }

    [Fact]
    public void Error_BusinessRule_CreatesCorrectError()
    {
        // Act
        var error = Error.BusinessRule("Cannot delete project with active tasks");

        // Assert
        Assert.Equal("Error.BusinessRule", error.Code);
        Assert.Equal("Cannot delete project with active tasks", error.Message);
    }

    [Fact]
    public void Error_Custom_CreatesCorrectError()
    {
        // Act
        var error = Error.Custom("MyCustomCode", "My custom message");

        // Assert
        Assert.Equal("MyCustomCode", error.Code);
        Assert.Equal("My custom message", error.Message);
    }

    [Fact]
    public void ResultExtensions_Match_Success_ExecutesSuccessFunction()
    {
        // Arrange
        var result = Result<int>.Success(42);
        var successCalled = false;
        var failureCalled = false;

        // Act
        var output = result.Match(
            onSuccess: value => { successCalled = true; return value * 2; },
            onFailure: error => { failureCalled = true; return 0; }
        );

        // Assert
        Assert.True(successCalled);
        Assert.False(failureCalled);
        Assert.Equal(84, output);
    }

    [Fact]
    public void ResultExtensions_Match_Failure_ExecutesFailureFunction()
    {
        // Arrange
        var error = Error.NotFound("Project", 5);
        var result = Result<int>.Failure(error);
        var successCalled = false;
        var failureCalled = false;

        // Act
        var output = result.Match(
            onSuccess: value => { successCalled = true; return value * 2; },
            onFailure: err => { failureCalled = true; return -1; }
        );

        // Assert
        Assert.False(successCalled);
        Assert.True(failureCalled);
        Assert.Equal(-1, output);
    }
}
