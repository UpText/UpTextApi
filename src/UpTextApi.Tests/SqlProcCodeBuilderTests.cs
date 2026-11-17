using System.Text.RegularExpressions;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class SqlProcCodeBuilderTests
{
    [TestCase("get", false)]
    [TestCase("get", true)]
    [TestCase("post", false)]
    [TestCase("post", true)]
    [TestCase("put", false)]
    [TestCase("put", true)]
    [TestCase("delete", false)]
    [TestCase("delete", true)]
    public void TenantTables_UseAuthenticatedTenantAcrossCrud(string verb, bool auth)
    {
        var sql = Build(Model(), verb, auth);

        Assert.That(sql, Does.Contain("@auth_tenant nvarchar(255)"));
        Assert.That(sql, Does.Not.Contain("@tenant "));
        Assert.That(sql, Does.Not.Contain("tenant = COALESCE"));
        AssertValidParameterLists(sql);

        if (verb == "post")
        {
            Assert.That(sql, Does.Contain("VALUES\n(\n    @auth_tenant,\n    @name"));
        }
        else
        {
            Assert.That(sql, Does.Contain("AND tenant = @auth_tenant"));
            if (verb is "put" or "delete")
            {
                Assert.That(Regex.Matches(sql, "AND tenant = @auth_tenant").Count, Is.EqualTo(2));
            }
        }

        if (verb is "post" or "put")
        {
            Assert.That(sql, Does.Contain("EXEC api.tags_Get @ID = @"));
            Assert.That(sql, Does.Contain(", @auth_tenant = @auth_tenant"));
            Assert.That(sql.Contains(", @auth_email = @auth_email"), Is.EqualTo(auth));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TablesWithoutTenant_DoNotAddTenantAuthentication(bool auth)
    {
        var sql = Build(Model(includeTenant: false), "all", auth);

        Assert.That(sql, Does.Not.Contain("auth_tenant"));
        Assert.That(sql.Contains("@auth_email NVARCHAR(255)"), Is.EqualTo(auth));
        AssertValidParameterLists(sql);
    }

    [TestCase("Tenant", "nvarchar", -1, "nvarchar(max)")]
    [TestCase("TENANT", "int", null, "int")]
    [TestCase("tenant", "uniqueidentifier", null, "uniqueidentifier")]
    public void TenantDetection_IsCaseInsensitiveAndPreservesType(string name, string type, int? length, string expected)
    {
        var model = Model();
        var tenant = model.Columns.Single(column => column.Name == "tenant");
        tenant.Name = name;
        tenant.SqlType = type;
        tenant.MaxLen = length;

        var sql = Build(model, "all", false);

        Assert.That(Regex.Matches(sql, $"@auth_tenant {Regex.Escape(expected)}(?:,|\\n)").Count, Is.EqualTo(4));
        Assert.That(sql, Does.Contain($"AND {name} = @auth_tenant"));
        Assert.That(sql, Does.Not.Contain($"@{name} "));
        AssertValidParameterLists(sql);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TenantOnlyTable_HasValidParametersAndNoEmptyUpdate(bool auth)
    {
        var model = Model();
        model.Columns.RemoveAll(column => column.Name == "name");
        model.NonKeyColumns.RemoveAll(column => column.Name == "name");

        var sql = Build(model, "all", auth);

        Assert.That(sql, Does.Not.Contain("UPDATE dbo.tags"));
        Assert.That(sql, Does.Contain("VALUES\n(\n    @auth_tenant\n)"));
        AssertValidParameterLists(sql);
    }

    [Test]
    public void GetWithSearchPagingAndSort_KeepsTenantFilterMandatory()
    {
        var sql = SqlProcCodeBuilder.BuildTableProc("api", "dbo", Model(), "get", true, true, true, true);
        Assert.That(sql, Does.Contain("WHERE (@ID IS NULL OR @ID = id) AND tenant = @auth_tenant"));
        Assert.That(sql, Does.Contain("  AND (@filter IS NULL"));
        Assert.That(sql, Does.Contain("OFFSET @first_row ROWS"));
    }

    private static string Build(SqlTableModel model, string verb, bool auth) =>
        SqlProcCodeBuilder.BuildTableProc("api", "dbo", model, verb, false, false, false, auth).Replace("\r\n", "\n");

    private static void AssertValidParameterLists(string sql)
    {
        Assert.That(Regex.IsMatch(sql, @",\s*\)"), Is.False, "Generated SQL must not have a trailing comma.");
    }

    private static SqlTableModel Model(bool includeTenant = true)
    {
        var model = new SqlTableModel { TableName = "tags", Resource = "tags", KeyColumn = "id", IsIdentity = true };
        model.Columns.Add(new SqlTableColumn { Name = "id", SqlType = "int" });
        if (includeTenant)
        {
            model.Columns.Add(new SqlTableColumn { Name = "tenant", SqlType = "nvarchar", MaxLen = 255 });
        }
        model.Columns.Add(new SqlTableColumn { Name = "name", SqlType = "nvarchar", MaxLen = 100 });
        model.NonKeyColumns.AddRange(model.Columns.Skip(1));
        return model;
    }
}
