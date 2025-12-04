namespace BLL.Common;

/// <summary>
/// Represents an error with a code and message
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    // General errors
    public static readonly Error NullValue = new("Error.NullValue", "The specified value is null");
    public static readonly Error ValidationFailed = new("Error.ValidationFailed", "Validation failed");
    public static readonly Error UnexpectedError = new("Error.Unexpected", "An unexpected error occurred");

    // Not Found errors
    public static Error NotFound(string entityName, int id) => 
        new("Error.NotFound", $"{entityName} with ID {id} was not found");

    public static Error NotFound(string entityName, string identifier) => 
        new("Error.NotFound", $"{entityName} '{identifier}' was not found");

    // Conflict errors
    public static Error Conflict(string message) => 
        new("Error.Conflict", message);

    // Validation errors
    public static Error Validation(string field, string message) => 
        new($"Error.Validation.{field}", message);

    // Authorization errors
    public static readonly Error Unauthorized = new("Error.Unauthorized", "User is not authorized");
    public static readonly Error Forbidden = new("Error.Forbidden", "Access is forbidden");

    // Business logic errors
    public static Error BusinessRule(string message) => 
        new("Error.BusinessRule", message);

    public static Error Custom(string code, string message) => 
        new(code, message);
}
