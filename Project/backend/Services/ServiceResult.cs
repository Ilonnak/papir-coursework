namespace Backend.Services;

public class ServiceResult<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = 200;
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public T? Data { get; init; }

    public static ServiceResult<T> Ok(T data, int statusCode = 200) => new()
    {
        Success = true,
        StatusCode = statusCode,
        Data = data
    };

    public static ServiceResult<T> Fail(string message, int statusCode = 400, string? errorCode = null, T? data = default) => new()
    {
        Success = false,
        StatusCode = statusCode,
        ErrorMessage = message,
        ErrorCode = errorCode,
        Data = data
    };
}
