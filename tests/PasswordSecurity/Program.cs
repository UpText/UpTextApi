using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using UpTextApi.Services;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}

const string password = "a long password with Unicode æ🔐";
var hash = UpHasher.HashPassword(password);
var otherHash = UpHasher.HashPassword(password);
Check(hash.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$"), "Argon2id profile");
Check(hash != otherHash, "Unique salt for each password hash");
Check(UpHasher.VerifyPassword(password, hash), "Correct password");
Check(UpHasher.VerifyPassword(password, otherHash), "Second salt verifies");
Check(!UpHasher.VerifyPassword("wrong password", hash), "Wrong password rejected");
Check(!UpHasher.VerifyPassword(null, hash), "Missing password rejected");
foreach (var invalid in new string?[]
{
    null, "", "invalid", hash.Replace("v=19", "v=16"),
    hash.Replace("m=19456", "m=2147483647"), hash.Replace("t=2", "t=200000"),
    hash.Replace("p=1", "p=256"), hash + "$extra", hash[..^1],
    hash[..^1] + "!", Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)))
})
    Check(!UpHasher.VerifyPassword(password, invalid), "Malformed/unsupported hash rejected");

foreach (var direction in new[] { ParameterDirection.Output, ParameterDirection.InputOutput, ParameterDirection.Input })
foreach (var status in new[] { 200, 201, 400, 401, 500 })
{
    using var command = new SqlCommand();
    command.Parameters.Add(new SqlParameter("@passwordHash", SqlDbType.NVarChar, 256)
    {
        Direction = direction, Value = hash
    });
    var valid = LoginPasswordVerifier.VerifyAndRemoveHash(command, password, status);
    Check(valid == (direction != ParameterDirection.Input && status < 300), "Login requires output hash and successful SQL status");
    Check(!command.Parameters.Contains("@passwordHash"), "Stored hash removed before response/JWT construction");
}
using (var command = new SqlCommand())
    Check(!LoginPasswordVerifier.VerifyAndRemoveHash(command, password, 200), "Missing output hash rejected");
foreach (var stored in new object[] { DBNull.Value, "bad hash", hash })
{
    using var command = new SqlCommand();
    command.Parameters.Add(new SqlParameter("@passwordHash", SqlDbType.NVarChar, 256)
    {
        Direction = ParameterDirection.InputOutput, Value = stored
    });
    Check(!LoginPasswordVerifier.VerifyAndRemoveHash(command, "wrong", 200), "Invalid login rejected");
}
// Exercise actual request binding using cached SQL metadata; no database is needed.
foreach (var isLogin in new[] { true, false })
foreach (var hasPassword in new[] { true, false })
{
    using var cache = new MemoryCache(new MemoryCacheOptions());
    using var command = new SqlCommand("[api].test_post");
    cache.Set(command.CommandText, new[]
    {
        new SqlParameter("@passwordHash", SqlDbType.NVarChar, 256)
        {
            Direction = isLogin ? ParameterDirection.InputOutput : ParameterDirection.Input
        }
    });
    var context = new DefaultHttpContext();
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("localhost");
    context.Request.QueryString = new QueryString("?passwordHash=attacker-query-hash");
    var body = new JsonObject { ["passwordHash"] = "attacker-body-hash" };
    if (hasPassword) body["password"] = password;
    var executor = new SqlWebApiExecutor(null!, cache, null!, null!, null!, null!);
    typeof(SqlWebApiExecutor).GetMethod("ApplyParameters", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(executor, new object?[] { command, context.Request, null, body.ToJsonString(), "{}", body, isLogin });
    var value = command.Parameters["@passwordHash"].Value;
    Check(isLogin || !hasPassword
        ? value == DBNull.Value
        : UpHasher.VerifyPassword(password, value as string), "Client hash injection ignored; writes hash plaintext and login clears output");
}
Console.WriteLine($"Passed {checks} password-security checks.");
