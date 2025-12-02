namespace BLL.Common;

/// <summary>
/// Represents the result of an operation that returns a value
/// </summary>
public class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public Error Error { get; }

    private Result(bool isSuccess, T? value, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Success result cannot have an error");
        
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Failure result must have an error");

        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Creates a success result with a value
    /// </summary>
    public static Result<T> Success(T value) => new(true, value, Error.None);

    /// <summary>
    /// Creates a failure result with an error
    /// </summary>
    public static Result<T> Failure(Error error) => new(false, default, error);

    /// <summary>
    /// Implicit conversion from T to Result<T>
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Implicit conversion from Error to Result<T>
    /// </summary>
    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>
/// Represents the result of an operation that doesn't return a value
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    private Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException("Success result cannot have an error");
        
        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException("Failure result must have an error");

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>
    /// Creates a success result
    /// </summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>
    /// Creates a failure result with an error
    /// </summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>
    /// Implicit conversion from Error to Result
    /// </summary>
    public static implicit operator Result(Error error) => Failure(error);
}
