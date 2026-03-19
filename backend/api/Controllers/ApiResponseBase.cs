namespace api.Controllers;
public record ApiResponseBase<T>(
    bool Success,
    T? Data,
    string? ErrorMessage = null);