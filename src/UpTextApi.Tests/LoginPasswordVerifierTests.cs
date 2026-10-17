using System.Data;
using Microsoft.Data.SqlClient;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class LoginPasswordVerifierTests
{
    private const string Password = "a long password with Unicode æ🔐";
    private string passwordHash = null!;

    [OneTimeSetUp]
    public void CreatePasswordHash()
    {
        passwordHash = UpHasher.HashPassword(Password);
    }

    [TestCase(ParameterDirection.Output)]
    [TestCase(ParameterDirection.InputOutput)]
    public void VerifyAndRemoveHash_WithMatchingPasswordAndOutputHash_ReturnsTrueAndRemovesHash(
        ParameterDirection direction)
    {
        using var command = CreateCommand(passwordHash, direction);

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, Password, 200);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
        });
    }

    [TestCase(199, false)]
    [TestCase(200, true)]
    [TestCase(201, true)]
    [TestCase(204, true)]
    [TestCase(299, true)]
    [TestCase(300, false)]
    [TestCase(401, false)]
    [TestCase(500, false)]
    public void VerifyAndRemoveHash_WithStatusCode_AcceptsOnlySuccessfulResponses(int statusCode, bool expected)
    {
        using var command = CreateCommand(passwordHash);

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, Password, statusCode);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
        });
    }

    [TestCase(ParameterDirection.Input)]
    [TestCase(ParameterDirection.ReturnValue)]
    public void VerifyAndRemoveHash_WithNonOutputHash_ReturnsFalseAndRemovesHash(ParameterDirection direction)
    {
        using var command = CreateCommand(passwordHash, direction);

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, Password, 200);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t\r\n")]
    [TestCase("wrong password")]
    [TestCase("A long password with Unicode æ🔐")]
    public void VerifyAndRemoveHash_WithInvalidPassword_ReturnsFalseAndRemovesHash(string? password)
    {
        using var command = CreateCommand(passwordHash);

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, password, 200);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-a-password-hash")]
    [TestCase(123)]
    public void VerifyAndRemoveHash_WithInvalidStoredHash_ReturnsFalseAndRemovesHash(object? storedHash)
    {
        using var command = CreateCommand(storedHash);

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, Password, 200);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
        });
    }

    [Test]
    public void VerifyAndRemoveHash_WithDatabaseNullHash_ReturnsFalseAndRemovesHash()
    {
        using var command = CreateCommand(DBNull.Value);

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, Password, 200);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
        });
    }

    [Test]
    public void VerifyAndRemoveHash_WithoutHashParameter_ReturnsFalseAndPreservesOtherParameters()
    {
        using var command = new SqlCommand();
        var userId = command.Parameters.AddWithValue("@userId", 42);

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, Password, 200);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(command.Parameters.Count, Is.EqualTo(1));
            Assert.That(command.Parameters["@userId"], Is.SameAs(userId));
            Assert.That(userId.Value, Is.EqualTo(42));
        });
    }

    [TestCase(Password, true)]
    [TestCase("wrong password", false)]
    public void VerifyAndRemoveHash_WithOtherParameters_RemovesOnlyPasswordHash(string password, bool expected)
    {
        using var command = CreateCommand(passwordHash);
        var userId = command.Parameters.AddWithValue("@userId", 42);
        var role = command.Parameters.AddWithValue("@role", "admin");

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, password, 200);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
            Assert.That(command.Parameters.Count, Is.EqualTo(2));
            Assert.That(command.Parameters["@userId"], Is.SameAs(userId));
            Assert.That(command.Parameters["@role"], Is.SameAs(role));
            Assert.That(userId.Value, Is.EqualTo(42));
            Assert.That(role.Value, Is.EqualTo("admin"));
        });
    }

    [TestCase(" leading and trailing spaces ", " leading and trailing spaces ", true)]
    [TestCase(" leading and trailing spaces ", "leading and trailing spaces", false)]
    [TestCase("   ", "   ", false)]
    public void VerifyAndRemoveHash_WithWhitespaceInPassword_PreservesSpacesAndRejectsWhitespaceOnly(
        string storedPassword, string suppliedPassword, bool expected)
    {
        using var command = CreateCommand(UpHasher.HashPassword(storedPassword));

        var result = LoginPasswordVerifier.VerifyAndRemoveHash(command, suppliedPassword, 200);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
        });
    }

    [TestCase(Password, true)]
    [TestCase("wrong password", false)]
    public void VerifyAndRemoveHash_WhenCalledAgain_CannotReuseRemovedHash(string password, bool expectedFirstResult)
    {
        using var command = CreateCommand(passwordHash);

        var firstResult = LoginPasswordVerifier.VerifyAndRemoveHash(command, password, 200);
        var secondResult = LoginPasswordVerifier.VerifyAndRemoveHash(command, Password, 200);

        Assert.Multiple(() =>
        {
            Assert.That(firstResult, Is.EqualTo(expectedFirstResult));
            Assert.That(secondResult, Is.False);
            Assert.That(command.Parameters.Contains("@passwordHash"), Is.False);
        });
    }

    private static SqlCommand CreateCommand(object? storedHash,
        ParameterDirection direction = ParameterDirection.Output)
    {
        var command = new SqlCommand();
        command.Parameters.Add(new SqlParameter("@passwordHash", SqlDbType.NVarChar, 256)
        {
            Direction = direction,
            Value = storedHash
        });
        return command;
    }
}
