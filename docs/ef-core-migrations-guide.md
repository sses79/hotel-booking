# EF Core Model and Migration Order

This project uses Entity Framework Core migrations to turn the C# data model
into a SQL Server database schema.

## Creation order

The normal development order is:

```text
Entity classes
    -> HotelBookingDbContext configuration
    -> EF Core migration files
    -> SQL Server database schema
```

### 1. Create or change entity classes

Define the data objects in `src/HotelBooking.Models`, such as `Hotel`, `Room`,
and `Booking`.

### 2. Configure the EF Core model

Update
`src/HotelBooking.Repository/Data/HotelBookingDbContext.cs`.

The context exposes the entities through `DbSet` properties:

```csharp
public DbSet<Hotel> Hotels => Set<Hotel>();
public DbSet<Room> Rooms => Set<Room>();
public DbSet<Booking> Bookings => Set<Booking>();
```

`OnModelCreating` configures tables, keys, relationships, indexes, column
types, maximum lengths, and database constraints.

### 3. Generate a migration

After changing an entity or the `DbContext` configuration, run this command
from the repository root:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <MigrationName> \
  --project src/HotelBooking.Repository/HotelBooking.Repository.csproj \
  --startup-project src/HotelBooking.Api/HotelBooking.Api.csproj \
  --output-dir Data/Migrations
```

For the first migration, `<MigrationName>` can be `InitialCreate`. Later names
should describe the schema change, for example `AddBookingStatus`.

EF Core compares the current model with the model snapshot and generates the
required migration files. For `InitialCreate`, it generated:

```text
20260709083405_InitialCreate.cs
20260709083405_InitialCreate.Designer.cs
HotelBookingDbContextModelSnapshot.cs
```

These files are generated together; you do not manually create one of them
first.

## Purpose of each generated file

### `<timestamp>_<MigrationName>.cs`

This is the main migration file.

- `Up` contains operations that apply the schema change.
- `Down` contains operations that reverse the schema change.

For example, `Up` may create a table while `Down` drops it. Review this file
before applying a migration. It can be edited carefully when EF Core's
generated database operations need customization.

### `<timestamp>_<MigrationName>.Designer.cs`

This contains EF Core metadata describing the complete model associated with
that particular migration. It is normally not edited manually.

The generated file can include:

```csharp
#pragma warning disable 612, 618
```

This suppresses obsolete-member warnings inside generated compatibility code.
It does not disable those warnings throughout the application.

### `HotelBookingDbContextModelSnapshot.cs`

This represents the latest EF Core model. When the next migration is created,
EF Core compares the new model against this snapshot to determine what
changed. It is normally not edited manually.

Each new migration adds a new timestamped migration pair and updates the one
model snapshot.

## Apply migrations to the database

Generating a migration does not change the database. Apply pending migrations
with:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update \
  --project src/HotelBooking.Repository/HotelBooking.Repository.csproj \
  --startup-project src/HotelBooking.Api/HotelBooking.Api.csproj
```

EF Core executes each pending migration's `Up` method in chronological order
and records applied migrations in the database's `__EFMigrationsHistory`
table.

## Practical workflow

For every database model change:

1. Change the entity classes and/or `HotelBookingDbContext` configuration.
2. Build the solution and fix compilation errors.
3. Generate a migration with a meaningful name.
4. Review the generated `Up` and `Down` methods.
5. Apply the migration to the local SQL Server database.
6. Run the relevant automated tests.
7. Commit the migration files with the model change.

Do not delete or rewrite migrations that may already have been applied to a
shared or deployed database. Create a new migration for the correction.

## Add a field to an existing model

Update the entity class first. For example, to add an optional floor number to
`Room`:

```csharp
public int? FloorNumber { get; set; }
```

If the property needs explicit database configuration, update
`ConfigureRooms` in `HotelBookingDbContext` as well:

```csharp
entity.Property(room => room.FloorNumber);
```

Then generate a new migration:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add AddRoomFloorNumber \
  --project src/HotelBooking.Repository/HotelBooking.Repository.csproj \
  --startup-project src/HotelBooking.Api/HotelBooking.Api.csproj \
  --output-dir Data/Migrations
```

Review the generated `Up` and `Down` methods, then apply the migration:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update \
  --project src/HotelBooking.Repository/HotelBooking.Repository.csproj \
  --startup-project src/HotelBooking.Api/HotelBooking.Api.csproj
```

The complete flow is:

```text
Room.cs
    -> HotelBookingDbContext.cs, if configuration is needed
    -> dotnet ef migrations add
    -> review the generated migration
    -> dotnet ef database update
```

### Adding a required field when data already exists

Be careful when adding a non-nullable property:

```csharp
public int FloorNumber { get; set; }
```

If the table already contains rows, SQL Server needs a value for the new
column in every existing row. A safe approach is:

1. Add the property as nullable: `public int? FloorNumber { get; set; }`.
2. Create and apply a migration.
3. Populate the field for all existing rows.
4. Change the property to non-nullable: `public int FloorNumber { get; set; }`.
5. Create and apply a second migration.

A database default can be used instead when there is a genuine default value
that is valid for every existing and future row. Review the generated
migration to ensure that EF Core handles existing rows as intended.

## Remove a field from an existing model

To remove a field:

1. Delete the property from the entity class.
2. Delete any corresponding configuration from `HotelBookingDbContext`.
3. Generate a new migration with a descriptive name.

For example:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add RemoveRoomFloorNumber \
  --project src/HotelBooking.Repository/HotelBooking.Repository.csproj \
  --startup-project src/HotelBooking.Api/HotelBooking.Api.csproj \
  --output-dir Data/Migrations
```

The generated `Up` method should contain an operation similar to:

```csharp
migrationBuilder.DropColumn(
    name: "FloorNumber",
    table: "Rooms");
```

Review it and then apply the migration with `dotnet ef database update` using
the command shown above.

Dropping a column permanently deletes the values stored in that column. Back
up or move important data before applying the migration.

Do not modify `InitialCreate` to represent a later model change after that
migration has been applied. Keep it as database history and add a new
migration instead.

## Production Azure migrations without a SQL password in GitHub

For a production-style Azure deployment, run migrations in a manual Azure
Container Apps Job using a managed identity. GitHub Actions authenticates to
Azure with OpenID Connect (OIDC) and starts the job; it does not connect to
Azure SQL or store a SQL password.

This section is an implementation blueprint. The repository currently applies
migrations through the destructive seed/reset service, so the components below
must be added before using this design.

```text
Pull request
    -> CI verifies that model changes have a committed migration

Merge to main
    -> publish API image tagged with the commit SHA
    -> publish migration image tagged with the same commit SHA
    -> GitHub exchanges its OIDC token for a short-lived Azure token
    -> update and start the Azure Container Apps migration job
    -> job authenticates to Azure SQL with its managed identity
    -> EF Core applies only migrations missing from __EFMigrationsHistory
    -> CD verifies that the job succeeded
    -> deploy the API image
```

Microsoft documents both
[OIDC authentication from GitHub Actions](https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-azure)
and
[manual Azure Container Apps Jobs](https://learn.microsoft.com/azure/container-apps/jobs).

### 1. Build a dedicated migration image

The current API image starts `HotelBooking.Api.dll`, so it is not a dedicated
finite migration process. Add a migration Dockerfile, for example
`src/HotelBooking.Api/Dockerfile.Migrations`, that builds an EF Core migration
bundle:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY .config/dotnet-tools.json .config/dotnet-tools.json
COPY src/HotelBooking.Api/HotelBooking.Api.csproj src/HotelBooking.Api/
COPY src/HotelBooking.Models/HotelBooking.Models.csproj src/HotelBooking.Models/
COPY src/HotelBooking.Repository/HotelBooking.Repository.csproj src/HotelBooking.Repository/
COPY src/HotelBooking.Services/HotelBooking.Services.csproj src/HotelBooking.Services/
RUN dotnet restore src/HotelBooking.Api/HotelBooking.Api.csproj
RUN dotnet tool restore

COPY src/ src/
RUN dotnet ef migrations bundle \
    --project src/HotelBooking.Repository/HotelBooking.Repository.csproj \
    --startup-project src/HotelBooking.Api/HotelBooking.Api.csproj \
    --configuration Release \
    --self-contained \
    --target-runtime linux-x64 \
    --output /app/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
WORKDIR /app
COPY --from=build /app/efbundle ./efbundle
COPY src/HotelBooking.Api/appsettings.json ./appsettings.json
ENTRYPOINT ["/app/efbundle", "--verbose"]
```

The process exits with success when all pending migrations have been applied
and exits with failure when a migration fails. Running the same bundle again
is safe because EF Core skips migrations already recorded in
`__EFMigrationsHistory`.

Publish this image to GHCR with an immutable commit-SHA tag, just like the API
image:

```text
ghcr.io/sses79/hotel-booking-migrations:<commit-sha>
```

Do not use `latest` for either production image. The API and migration image
for a release should use the same source commit.

### 2. Enable managed-identity authentication in SqlClient

Add the Azure authentication extension required by current versions of
`Microsoft.Data.SqlClient`:

```bash
dotnet add src/HotelBooking.Repository/HotelBooking.Repository.csproj \
  package Microsoft.Data.SqlClient.Extensions.Azure
```

Use a passwordless connection string for the migration job:

```text
Server=tcp:<server>.database.windows.net,1433;
Initial Catalog=HotelBooking;
Encrypt=True;
TrustServerCertificate=False;
Authentication=Active Directory Managed Identity;
User ID=<migration-managed-identity-client-id>;
```

`User ID` selects the user-assigned managed identity by client ID. There is no
password in this connection string. See
[SqlClient Microsoft Entra authentication](https://learn.microsoft.com/sql/connect/ado-net/sql/azure-active-directory-authentication)
and
[managed identity for Azure SQL](https://learn.microsoft.com/azure/azure-sql/database/authentication-azure-ad-user-assigned-managed-identity).

### 3. Provision a user-assigned identity and manual job with Bicep

A user-assigned identity is preferable here because it has a stable identity
before the job exists. Add resources similar to the following to the Bicep
deployment. The API version should be kept aligned with the version supported
by the Azure CLI and subscription when this is implemented.

```bicep
param migrationImageRepository string = 'ghcr.io/sses79/hotel-booking-migrations'
param migrationImageTag string

var migrationJobName = 'job-${projectName}-migrate-${environmentName}-${suffix}'

resource migrationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${projectName}-migrate-${environmentName}-${suffix}'
  location: location
  tags: tags
}

var migrationConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;User ID=${migrationIdentity.properties.clientId};'

resource migrationJob 'Microsoft.App/jobs@2025-01-01' = {
  name: migrationJobName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migrationIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: '${migrationImageRepository}:${migrationImageTag}'
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__HotelBooking'
              value: migrationConnectionString
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
  dependsOn: [
    sqlDatabase
  ]
}

output migrationJobName string = migrationJob.name
output migrationIdentityName string = migrationIdentity.name
output migrationIdentityClientId string = migrationIdentity.properties.clientId
```

The connection string contains identifiers but no credential, so it does not
need to be a password secret. Do not put the SQL administrator connection
string in this job.

If GHCR remains public, Azure can pull the image anonymously. For a private
registry, assign a pull identity and the minimum registry pull permission.

### 4. Grant the identity permissions inside Azure SQL once

Azure role assignments and SQL database permissions are different systems.
Attaching the managed identity to the job does not automatically create an
Azure SQL user.

After Bicep creates the user-assigned identity, connect to the `HotelBooking`
database as the configured Microsoft Entra administrator and run this one-time
bootstrap:

```sql
CREATE USER [<migration-managed-identity-name>] FROM EXTERNAL PROVIDER;

ALTER ROLE db_ddladmin ADD MEMBER [<migration-managed-identity-name>];
ALTER ROLE db_datareader ADD MEMBER [<migration-managed-identity-name>];
ALTER ROLE db_datawriter ADD MEMBER [<migration-managed-identity-name>];
```

The migration identity needs DDL permissions for schema changes and may need
data permissions for migration backfills and the migration-history table.
Production teams can replace these fixed roles with a reviewed custom
least-privilege permission set.

Run this bootstrap in each target database. Bicep cannot natively create a
contained database user because that is a SQL data-plane operation. Keep the
one-time command in an audited administrator runbook rather than passing a SQL
administrator password to GitHub.

Microsoft documents `CREATE USER ... FROM EXTERNAL PROVIDER` in its
[Azure SQL Microsoft Entra configuration guide](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-configure).

### 5. Configure GitHub-to-Azure OIDC

Create a Microsoft Entra application or user-assigned identity for the GitHub
deployment workflow, then add a federated credential restricted to this
repository and its `production` GitHub Environment. No Azure client secret is
required.

Store these identifiers as GitHub Environment variables or secrets:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
AZURE_RESOURCE_GROUP
AZURE_MIGRATION_JOB_NAME
```

The GitHub deployment identity needs only the Azure control-plane permissions
required to update, start, and read the job, plus whatever separate permission
is required to deploy the API. It does not need to be an Azure SQL user.

Protect the `production` Environment with required reviewers and allow it only
from `main`.

### 6. Start and verify the job from CD

The production job requests a short-lived OIDC token and uses `azure/login`:

```yaml
name: Deploy production

on:
  push:
    branches: [main]

concurrency:
  group: production-deployment
  cancel-in-progress: false

permissions:
  contents: read
  id-token: write

jobs:
  migrate:
    runs-on: ubuntu-latest
    environment: production
    steps:
      - uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: Start migration job
        run: >-
          az containerapp job start
          --name "${{ vars.AZURE_MIGRATION_JOB_NAME }}"
          --resource-group "${{ vars.AZURE_RESOURCE_GROUP }}"
```

Starting a job is asynchronous. The real workflow must capture the execution
name, poll it with `az containerapp job execution show`, and fail unless its
final state is `Succeeded`. Do not deploy the API merely because the start
request was accepted. The Azure CLI provides
[`job execution list` and `job execution show`](https://learn.microsoft.com/cli/azure/containerapp/job/execution)
for this verification.

The workflow should not contain `PRODUCTION_SQL_CONNECTION_STRING`; the
migration job obtains its database identity from Azure.

### 7. Preserve release ordering

For an additive, backward-compatible migration, use this order:

```text
Publish immutable images
    -> point migration job at the new migration image
    -> run job and require Succeeded
    -> deploy the new API image
    -> run health and smoke checks
```

Do not update the API to the new image in the same initial Bicep operation if
that could start the API before its required migration finishes. Keep
infrastructure provisioning separate from release orchestration, or update
the job image, run it, and update the API image as distinct CD steps.

For a breaking change, use expand/contract across releases:

1. Add a backward-compatible schema alongside the old schema.
2. Deploy an API version that works during the transition.
3. Backfill and verify data.
4. Switch all application usage to the new schema.
5. Remove the old schema in a later migration.

### 8. Remove migration behavior from seed/reset

Once the migration job is responsible for production schema updates, do not
use `POST /api/admin/seed` or `POST /api/admin/reset` in production. Both are
test-support operations and delete application data in this project.

The API's normal runtime database identity should also be separate from the
migration identity. Give the API only the data permissions it needs; only the
migration job should have schema-change permissions.
