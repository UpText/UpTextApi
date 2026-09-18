namespace UpTextApi.Services;

public sealed record SqlLogRow(
    int Id,
    string? ApiName,
    string? SwaServer,
    int? MsUsed,
    DateTime? TimeStamp,
    int? ReturnValue,
    string? RequestBody,
    string? ReturnBody,
    string? ExecString,
    string? Jwt,
    string? UnexpectedError);
