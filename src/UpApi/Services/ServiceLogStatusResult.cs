namespace UpApi.Services;

public sealed record ServiceLogStatusResult(
    long CallsLastHours,
    long UserErrorsLastHour,
    long SqlErrorsLastHour,
    long CallsToDay,
    long UserErrorsToDay,
    long SqlErrorsToDay,
    long Calls,
    long UserErrors,
    long SqlErrors);
