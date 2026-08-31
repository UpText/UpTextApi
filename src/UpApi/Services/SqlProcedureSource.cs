namespace UpApi.Services;

public sealed record SqlProcedureSource(
    string ProcedureName,
    string Definition);
