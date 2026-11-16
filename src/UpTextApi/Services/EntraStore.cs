using System.Data;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using UpTextApi.Configuration;

namespace UpTextApi.Services;

// Authentication procedures deliberately live outside the generic REST schema.
public sealed class EntraStore(IServiceConfigResolver services, IConfiguration configuration)
{
    private string Service => configuration["Entra:Service"] ?? "crmapi";

    private SqlConnection Connection()
    {
        if (!services.TryGet(Service, out var service) || service.SqlSchema != "crmapi")
            throw new InvalidOperationException("Entra:Service must reference the CRM service (crmapi schema).");
        return new(service.SqlConnectionString);
    }

    public async Task<JsonArray> Execute(string procedure, CancellationToken ct, params (string Name, object? Value)[] args)
    {
        await using var connection = Connection();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("crmauth." + procedure, connection) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new JsonArray();
        while (await reader.ReadAsync(ct))
        {
            var row = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : System.Text.Json.JsonSerializer.SerializeToNode(reader.GetValue(i));
            rows.Add(row);
        }
        return rows;
    }

    public async Task<EntraSettings> Settings(string tenant, CancellationToken ct)
    {
        var rows = await Execute("configuration_get", ct, ("@tenant", tenant));
        return EntraSettings.Read(rows.FirstOrDefault()?["config"]?.GetValue<string>());
    }

    public async Task<JsonObject?> User(string tenant, int id, CancellationToken ct,
        Guid? directory = null, Guid? oid = null, Guid? client = null)
    {
        var rows = await Execute("user_get", ct, ("@tenant", tenant), ("@id", id),
            ("@directoryId", directory), ("@objectId", oid), ("@clientId", client));
        return rows.FirstOrDefault() as JsonObject;
    }

    public string ServiceName => Service;
}
