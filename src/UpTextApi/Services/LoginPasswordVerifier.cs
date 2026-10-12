using System.Data;
using Microsoft.Data.SqlClient;

namespace UpTextApi.Services;

internal static class LoginPasswordVerifier
{
    public static bool VerifyAndRemoveHash(SqlCommand command, string? password, int statusCode)
    {
        if (!command.Parameters.Contains("@passwordHash"))
            return false;

        var parameter = command.Parameters["@passwordHash"];
        // Remove before output payload construction so the hash cannot become a JWT claim or response.
        command.Parameters.Remove(parameter);
        return statusCode is >= 200 and < 300 &&
               parameter.Direction is ParameterDirection.Output or ParameterDirection.InputOutput &&
               !string.IsNullOrWhiteSpace(password) &&
               UpHasher.VerifyPassword(password, parameter.Value as string);
    }
}
