namespace SyncUpRocks.Api.Controllers;

public record ApiResponseBase<T>(
    bool Success,
    T? Data,
    string? ErrorMessage = null);

public record ApiResponseDefault(bool Success = true, string? ErrorMessage = null);