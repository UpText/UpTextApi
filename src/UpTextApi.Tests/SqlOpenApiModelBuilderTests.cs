using System.Data;
using System.Net.Http;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class SqlOpenApiModelBuilderTests
{
    [Test]
    public void ConstructModelFromMetadata_WithNoRows_ReturnsEmptyModel()
    {
        using var metadata = Metadata();

        Assert.That(SqlOpenApiModelBuilder.ConstructModelFromMetadata(metadata).Controllers, Is.Empty);
    }

    [TestCase("customers_get")]
    [TestCase("customersget")]
    [TestCase("_CUSTOMERS_GET")]
    [TestCase("customers_put")]
    [TestCase("customers_post")]
    [TestCase("customers_delete")]
    [TestCase("customers_openapi")]
    [TestCase("customers_ui")]
    public void ConstructModelFromMetadata_WithSupportedProcedure_CreatesNormalizedController(string procedure)
    {
        using var metadata = Metadata();
        Add(metadata, procedure);

        var model = SqlOpenApiModelBuilder.ConstructModelFromMetadata(metadata);

        Assert.That(model.Controllers, Has.Count.EqualTo(1));
        var controller = model.Controllers[0];
        Assert.Multiple(() =>
        {
            Assert.That(controller.Name, Is.EqualTo("customers"));
            Assert.That(controller.Procs, Has.Count.EqualTo(1));
            Assert.That(controller.Procs[0].Name, Is.EqualTo(procedure.TrimStart('_').ToLowerInvariant()));
            Assert.That(controller.Procs[0].Parameters, Is.Empty);
        });
    }

    [TestCase("customers_search")]
    [TestCase("customers_get_history")]
    [TestCase("")]
    public void ConstructModelFromMetadata_WithUnsupportedProcedure_IgnoresRow(string procedure)
    {
        using var metadata = Metadata();
        Add(metadata, procedure, "@id");

        Assert.That(SqlOpenApiModelBuilder.ConstructModelFromMetadata(metadata).Controllers, Is.Empty);
    }

    [Test]
    public void ConstructModelFromMetadata_WithRelatedProceduresAndMultipleParameters_GroupsWithoutDuplicates()
    {
        using var metadata = Metadata();
        Add(metadata, "_CUSTOMERS_GET", "@id", "int");
        Add(metadata, "customers_get", "@name", "nvarchar");
        Add(metadata, "customers_post");
        Add(metadata, "orders_get");

        var model = SqlOpenApiModelBuilder.ConstructModelFromMetadata(metadata);

        Assert.That(model.Controllers.Select(c => c.Name), Is.EqualTo(new[] { "customers", "orders" }));
        var customers = model.Controllers[0];
        Assert.Multiple(() =>
        {
            Assert.That(customers.Procs.Select(p => p.Name),
                Is.EqualTo(new[] { "customers_get", "customers_post" }));
            Assert.That(customers.Procs[0].Parameters.Select(p => p.Name),
                Is.EqualTo(new[] { "@id", "@name" }));
            Assert.That(customers.Procs[1].Parameters, Is.Empty);
            Assert.That(model.Controllers[1].Procs, Has.Count.EqualTo(1));
        });
    }

    [TestCase("IN", false)]
    [TestCase("OUT", true)]
    [TestCase("INOUT", true)]
    public void ConstructModelFromMetadata_WithParameter_CopiesSqlMetadataAndDirection(string mode, bool isOutput)
    {
        using var metadata = Metadata();
        metadata.Rows.Add("orders_post", "@amount", "decimal", (byte)18, (short)4, -1, mode);

        var parameter = SqlOpenApiModelBuilder.ConstructModelFromMetadata(metadata)
            .Controllers.Single().Procs.Single().Parameters.Single();

        Assert.Multiple(() =>
        {
            Assert.That(parameter.Name, Is.EqualTo("@amount"));
            Assert.That(parameter.SqlType, Is.EqualTo("decimal"));
            Assert.That(parameter.Precision, Is.EqualTo(18));
            Assert.That(parameter.Scale, Is.EqualTo(4));
            Assert.That(parameter.MaxLen, Is.EqualTo(-1));
            Assert.That(parameter.IsOutput, Is.EqualTo(isOutput));
        });
    }

    [Test]
    public void ConstructModelFromMetadata_WithDatabaseNullMetadata_UsesEmptyStringAndZeroDefaults()
    {
        using var metadata = Metadata();
        metadata.Rows.Add("orders_get", "@value", DBNull.Value, DBNull.Value,
            DBNull.Value, DBNull.Value, "IN");

        var parameter = SqlOpenApiModelBuilder.ConstructModelFromMetadata(metadata)
            .Controllers.Single().Procs.Single().Parameters.Single();

        Assert.Multiple(() =>
        {
            Assert.That(parameter.SqlType, Is.Empty);
            Assert.That(parameter.Precision, Is.Zero);
            Assert.That(parameter.Scale, Is.Zero);
            Assert.That(parameter.MaxLen, Is.Zero);
            Assert.That(parameter.IsOutput, Is.False);
        });
    }

    [TestCase("customers_get", "GET")]
    [TestCase("customersput", "PUT")]
    [TestCase("CUSTOMERS_POST", "POST")]
    [TestCase("customers_delete", "DELETE")]
    [TestCase("customers_openapi", null)]
    [TestCase("customers_ui", null)]
    public void ConstructModelFromMetadata_WithProcedure_AssignsExpectedHttpVerb(string procedure, string? verb)
    {
        using var metadata = Metadata();
        Add(metadata, procedure);

        var proc = SqlOpenApiModelBuilder.ConstructModelFromMetadata(metadata)
            .Controllers.Single().Procs.Single();

        Assert.That(proc.GetVerb(), Is.EqualTo(verb is null ? null : new HttpMethod(verb)));
    }

    private static DataTable Metadata()
    {
        var table = new DataTable();
        table.Columns.Add("ProcedureName", typeof(string));
        table.Columns.Add("ParameterName", typeof(string));
        table.Columns.Add("SqlType", typeof(string));
        table.Columns.Add("Precision", typeof(object));
        table.Columns.Add("Scale", typeof(object));
        table.Columns.Add("MaxLen", typeof(object));
        table.Columns.Add("ParameterMode", typeof(string));
        return table;
    }

    private static void Add(DataTable metadata, string procedure,
        string parameter = "<no params>", string sqlType = "int") =>
        metadata.Rows.Add(procedure, parameter, sqlType, DBNull.Value, DBNull.Value, DBNull.Value, "IN");
}
