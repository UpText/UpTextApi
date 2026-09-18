using System.Text.Json.Serialization;

namespace UpTextApi.Services;

internal sealed class SqlWebApiErrorResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
