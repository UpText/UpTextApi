# Developing With UpTextApi: `myapi.products`

This script walks through creating a tiny SQL-backed API in UpTextApi. The demo exposes a `products` resource with only two fields:

- `Id`
- `Name`

The API service is called `myapi`, and its stored procedures live in the SQL schema `myapi`.

## 1. Create A Demo Product Table

Run this in SQL Server as a user with permission to create schemas, users, and tables.

```sql
USE master;
GO

IF DB_ID('UpTextApiDemo') IS NULL
BEGIN
    CREATE DATABASE UpTextApiDemo;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = 'upservice')
BEGIN
    CREATE LOGIN upservice WITH PASSWORD = 'VerySecret!321';
END;
GO

USE UpTextApiDemo;
GO

IF OBJECT_ID('dbo.products', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.products;
END;
GO

CREATE TABLE dbo.products
(
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name nvarchar(100) NOT NULL
);
GO
```

## 2. Fill The Table With Demo Data

```sql
INSERT INTO dbo.products (Name)
VALUES
    ('Notebook'),
    ('Keyboard'),
    ('Monitor'),
    ('Mouse'),
    ('USB-C Dock'),
    ('Webcam'),
    ('Headphones'),
    ('Laptop Stand');
GO

SELECT Id, Name
FROM dbo.products
ORDER BY Id;
GO
```

## 3. Create A Custom API Schema Called `myapi`

UpTextApi maps a configured service to a SQL schema. The service name in the URL will be `myapi`, and UpTextApi will execute procedures from the SQL schema named `myapi`.

```sql
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'myapi')
BEGIN
    EXEC('CREATE SCHEMA myapi AUTHORIZATION dbo;');
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'upservice')
BEGIN
    CREATE USER upservice FOR LOGIN upservice;
END;
GO
```

Configure UpTextApi with the same service name and schema:

```bash
docker run --rm \
  -p 5092:8080 \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e JWT_SECRET=replace-with-a-long-random-secret \
  -e JWT_ISSUER=UpTextApi \
  -e JWT_AUDIENCE=UpTextApiClient \
  -e JWT_HOURS=8 \
  -e Services__myapi__SqlSchema=myapi \
  -e Services__myapi__SqlConnectionString="Server=host.docker.internal,1433;Initial Catalog=UpTextApiDemo;User ID=upservice;Password=VerySecret!321;Encrypt=False;TrustServerCertificate=True;" \
  uptext/uptextapi
```

## 4. Give `upservice` Execute Access To The Schema

The service account only needs to connect to the database and execute procedures in the API schema.

```sql
GRANT CONNECT TO upservice;
GRANT EXECUTE ON SCHEMA::[myapi] TO upservice;
GO
```

If the procedures read or write tables owned by `dbo`, make sure ownership chaining is available through the procedure owner. Keep direct table access away from the service account unless you explicitly need it.

## 5. Add The First Read Procedure: `myapi.products_get`

UpTextApi uses the HTTP method to decide which stored procedure to call:

```text
GET /myapi/products
```

calls:

```text
myapi.products_get
```

Create the first version:

```sql
CREATE OR ALTER PROCEDURE myapi.products_get
    @Id varchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdInt int = TRY_CAST(@Id AS int);

    SELECT
        Id,
        Name
    FROM dbo.products
    WHERE @IdInt IS NULL OR Id = @IdInt
    ORDER BY Id;

    RETURN 200;
END;
GO
```

## 6. Execute It From A Browser

Start UpTextApi, then open:

```text
http://localhost:5092/myapi/products
```

To read one product by id:

```text
http://localhost:5092/myapi/products/1
```

The route convention is:

```text
/{service}/{resource}
/{service}/{resource}/{id}
```

So `myapi` is the service, `products` is the resource, and `1` is passed to the stored procedure as `@Id`.

## 7. Use The Home Page And Swagger

Open the home page:

```text
http://localhost:5092/
```

The home page shows configured services and links to tools such as Swagger UI, SQL generator, and SQL logs when they are enabled.

Open Swagger UI for this service:

```text
http://localhost:5092/docs/myapi
```

Open the generated OpenAPI JSON directly:

```text
http://localhost:5092/myapi/swagger.json
```

## 8. Add Server-Side Paging, Sorting, And Search

UpTextApi recognizes these special parameters on `GET` procedures:

- `@first_row`
- `@last_row`
- `@sort_field`
- `@sort_order`
- `@filter`
- `@total_rows OUTPUT`

Replace `myapi.products_get` with this version:

```sql
CREATE OR ALTER PROCEDURE myapi.products_get
    @Id varchar(max) = NULL,
    @first_row int = NULL,
    @last_row int = NULL,
    @sort_field nvarchar(128) = NULL,
    @sort_order nvarchar(4) = NULL,
    @filter nvarchar(max) = NULL,
    @total_rows int = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdInt int = TRY_CAST(@Id AS int);
    DECLARE @SortField nvarchar(128) = LOWER(COALESCE(@sort_field, 'id'));
    DECLARE @SortOrder nvarchar(4) = UPPER(COALESCE(@sort_order, 'ASC'));
    DECLARE @Offset int = CASE WHEN @first_row IS NULL OR @first_row < 0 THEN 0 ELSE @first_row END;
    DECLARE @Fetch int = CASE
        WHEN @last_row IS NULL OR @last_row < @Offset THEN 2147483647
        ELSE (@last_row - @Offset) + 1
    END;

    SELECT @total_rows = COUNT(*)
    FROM dbo.products
    WHERE (@IdInt IS NULL OR Id = @IdInt)
      AND (@filter IS NULL OR Name LIKE '%' + @filter + '%');

    SELECT
        Id,
        Name
    FROM dbo.products
    WHERE (@IdInt IS NULL OR Id = @IdInt)
      AND (@filter IS NULL OR Name LIKE '%' + @filter + '%')
    ORDER BY
        CASE WHEN @SortField = 'name' AND @SortOrder = 'ASC' THEN Name END ASC,
        CASE WHEN @SortField = 'name' AND @SortOrder = 'DESC' THEN Name END DESC,
        CASE WHEN @SortField = 'id' AND @SortOrder = 'DESC' THEN Id END DESC,
        Id ASC
    OFFSET @Offset ROWS
    FETCH NEXT @Fetch ROWS ONLY;

    RETURN 200;
END;
GO
```

Try paging, sorting, and search in a browser:

```text
http://localhost:5092/myapi/products?range=[0,2]&sort=["Name","ASC"]&filter=o
```

For collection `GET` responses, UpTextApi also returns a `Content-Range` header.

## 9. Add `POST`

`POST /myapi/products` calls `myapi.products_post`.

```sql
CREATE OR ALTER PROCEDURE myapi.products_post
    @Name nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.products (Name)
    VALUES (@Name);

    DECLARE @NewId varchar(max) = CONVERT(varchar(max), SCOPE_IDENTITY());

    EXEC myapi.products_get @Id = @NewId;
    RETURN 200;
END;
GO
```

Command-line example:

```bash
curl -X POST http://localhost:5092/myapi/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Desk Lamp"}'
```

## 10. Add `PUT`

`PUT /myapi/products/1` calls `myapi.products_put` and passes `1` as `@Id`.

```sql
CREATE OR ALTER PROCEDURE myapi.products_put
    @Id varchar(max),
    @Name nvarchar(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdInt int = TRY_CAST(@Id AS int);

    IF @IdInt IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.products WHERE Id = @IdInt)
    BEGIN
        RAISERROR('Unknown product', 1, 1);
        RETURN 404;
    END;

    UPDATE dbo.products
    SET Name = COALESCE(@Name, Name)
    WHERE Id = @IdInt;

    EXEC myapi.products_get @Id = @Id;
    RETURN 200;
END;
GO
```

Command-line example:

```bash
curl -X PUT http://localhost:5092/myapi/products/1 \
  -H "Content-Type: application/json" \
  -d '{"name":"Premium Notebook"}'
```

## 11. Add `DELETE`

`DELETE /myapi/products/1` calls `myapi.products_delete` and passes `1` as `@Id`.

```sql
CREATE OR ALTER PROCEDURE myapi.products_delete
    @Id varchar(max)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IdInt int = TRY_CAST(@Id AS int);

    IF @IdInt IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.products WHERE Id = @IdInt)
    BEGIN
        RAISERROR('Unknown product', 1, 1);
        RETURN 404;
    END;

    DELETE FROM dbo.products
    WHERE Id = @IdInt;

    RETURN 200;
END;
GO
```

Command-line example:

```bash
curl -X DELETE http://localhost:5092/myapi/products/1
```

## 12. Use Swagger To Try The Functions

Open:

```text
http://localhost:5092/docs/myapi
```

Then use Swagger UI:

1. Expand `GET /myapi/products`.
2. Click `Try it out`.
3. Enter optional values for `range`, `sort`, and `filter`.
4. Click `Execute`.
5. Expand `POST /myapi/products`, click `Try it out`, enter a JSON body, and execute it.
6. Use `PUT /myapi/products/{id}` to update an existing product.
7. Use `DELETE /myapi/products/{id}` to delete a product.

Example request bodies:

```json
{
  "name": "Desk Lamp"
}
```

```json
{
  "name": "Premium Notebook"
}
```

## Optional: Add Better Swagger Text

Create `myapi.products_openapi` to enrich the generated documentation.

```sql
CREATE OR ALTER PROCEDURE myapi.products_openapi
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        operation,
        class,
        name,
        property,
        value
    FROM (VALUES
        ('get',    'operation', '',       'summary',     'Get one product or a paged list of products'),
        ('post',   'operation', '',       'summary',     'Create a product'),
        ('put',    'operation', '',       'summary',     'Update a product'),
        ('delete', 'operation', '',       'summary',     'Delete a product'),
        ('*',      'parameter', 'Id',     'description', 'Product id from the route'),
        ('get',    'parameter', 'filter', 'description', 'Searches product names'),
        ('get',    'parameter', 'sort',   'description', 'Example: ["Name","ASC"]'),
        ('get',    'parameter', 'range',  'description', 'Example: [0,24] for server-side paging'),
        ('*',      'response',  '404',    'description', 'Product not found')
    ) AS Data(operation, class, name, property, value);
END;
GO
```
