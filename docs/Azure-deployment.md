# Azure Deployment

This runbook deploys the Hotel Booking API to the small, optional Azure
environment defined in `infra/bicep/main.bicep`.

```text
Public GHCR image
  -> Azure Container Apps Consumption
  -> Azure SQL Database serverless
```

The deployment deliberately does not create Azure Container Registry, Key
Vault, Application Insights, or Log Analytics.

The development parameters configure the repository owner's tenant identity as
the Azure SQL Microsoft Entra administrator. This enables portal Query Editor
access while the Container App continues to use its SQL authentication secret.

The development deployment creates `HotelBookingFree` with the Azure SQL
Database monthly free allowance. The subscription must be eligible and have a
free-database slot available. If the free allowance is exhausted, the database
continues at standard serverless rates so the interview demo remains available.

## Portal Query Editor

Query Editor requires both:

- A Microsoft Entra administrator on the SQL server.
- A SQL firewall rule for the user's current public IP address.

The Entra administrator is managed by Bicep. Add a narrow firewall rule when
the current client IP changes:

```bash
az sql server firewall-rule create \
  --resource-group rg-hotel-booking-dev-uk-south \
  --server sql-hotel-booking-dev-qjygtiyk \
  --name DeveloperClient \
  --start-ip-address '<current-public-ip>' \
  --end-ip-address '<current-public-ip>'
```

Remove local access when it is no longer needed:

```bash
az sql server firewall-rule delete \
  --resource-group rg-hotel-booking-dev-uk-south \
  --server sql-hotel-booking-dev-qjygtiyk \
  --name DeveloperClient
```

Serverless SQL may need a short time to resume before Query Editor connects.

## Prerequisites

- Azure CLI with Bicep support.
- Docker, for checking that the GHCR image is publicly readable.
- An Azure subscription selected with `az account set`.
- A successful `Publish API image` GitHub Actions run from `main`.
- The GHCR package configured as public.

Confirm the active subscription before creating resources:

```bash
az account show \
  --query '{subscription:name, subscriptionId:id, user:user.name}' \
  --output table
```

## Deployment Values

Create an ignored `.env.azure` file in the repository root:

```bash
API_IMAGE_TAG='<main-commit-sha>'
AZURE_SQL_ADMIN_PASSWORD='<strong-unique-password>'
```

Use the exact SHA tag published by GitHub Actions. Do not use `latest`, and do
not reuse the local SQL Server password.

Load the values into the current shell:

```bash
set -a
source .env.azure
set +a
```

The committed `dev.bicepparam` file reads these environment variables. The SQL
password is declared as a secure Bicep parameter and is stored in the Container
App as a secret.

Verify that the image can be pulled anonymously:

```bash
docker buildx imagetools inspect \
  "ghcr.io/sses79/hotel-booking-api:${API_IMAGE_TAG}"
```

## Create Resource Group

The resource group itself does not have a charge:

```bash
az group create \
  --name rg-hotel-booking-dev-uk-south \
  --location uksouth \
  --tags project=hotel-booking environment=dev managedBy=bicep
```

## Validate And Preview

Compile the Bicep template:

```bash
az bicep build --file infra/bicep/main.bicep
```

Ask Azure to validate provider and regional rules:

```bash
az deployment group validate \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam
```

Preview the exact resource changes:

```bash
az deployment group what-if \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam
```

The first deployment should create:

- One Container Apps environment with no application-log destination.
- One Container App using the immutable GHCR image.
- One Azure SQL logical server.
- One `HotelBookingFree` Azure SQL serverless database using the monthly free
  allowance and 15-minute auto-pause.
- One SQL firewall rule allowing Azure services.

It should not create Application Insights or Log Analytics.

## Deploy

```bash
az deployment group create \
  --name main \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam \
  --output table
```

The deployment output includes `apiUrl`, `apiAppName`, `sqlServerName`, and
`sqlDatabaseName`.

## Check Azure Resources

Run the read-only resource checker:

```bash
./scripts/check-azure-resources.sh
```

Optional overrides:

```bash
RESOURCE_GROUP=rg-hotel-booking-dev-uk-south \
DEPLOYMENT_NAME=main \
./scripts/check-azure-resources.sh
```

The script verifies:

- Required resources exist and provisioning succeeded.
- The Container App uses a SHA-tagged GHCR image.
- Scale is configured from zero to two replicas.
- No application-log destination is configured.
- Azure SQL uses the serverless SKU, monthly free allowance, paid-overage safety
  behavior, and 15-minute auto-pause.
- The Azure SQL server has a Microsoft Entra administrator.
- Application Insights and Log Analytics are absent.
- The deployed `/health` endpoint returns `Healthy`.

## Seed And Smoke Test

Get the API URL:

```bash
API_URL="$(az deployment group show \
  --name main \
  --resource-group rg-hotel-booking-dev-uk-south \
  --query properties.outputs.apiUrl.value \
  --output tsv)"
```

Create the schema and predictable test data:

```bash
curl --fail --request POST "${API_URL}/api/admin/seed"
```

Check the seeded hotel:

```bash
curl --fail "${API_URL}/api/hotels?name=Grand"
```

Open Swagger at:

```text
<apiUrl>/swagger
```

Azure SQL can take a little longer on the first seed request or after resuming
from auto-pause. The seed endpoint applies pending EF Core migrations before
resetting and creating test data.

## Cut Over From The Legacy Paid Database

Changing `sqlDatabaseName` from `HotelBooking` to `HotelBookingFree` creates a
new database during an incremental Bicep deployment. The same deployment updates
the Container App secret so new API revisions connect to `HotelBookingFree`.

The old `HotelBooking` database is deliberately left in place for rollback.
Azure SQL Database has no stop operation that removes all charges: a paused
serverless database stops compute billing but continues to incur provisioned
storage charges.

Complete these checks before deleting the old database:

1. Call `POST /api/admin/seed` so the new database applies migrations and creates
   predictable data.
2. Run `./scripts/check-azure-resources.sh`.
3. Create a booking through Swagger and retrieve it by reference.
4. Confirm the deployment output reports `HotelBookingFree`.

```bash
az deployment group show \
  --name main \
  --resource-group rg-hotel-booking-dev-uk-south \
  --query properties.outputs.sqlDatabaseName.value \
  --output tsv
```

The following command permanently removes the legacy database and its demo
bookings. Run it only after the new database passes the smoke tests:

```bash
az sql db delete \
  --resource-group rg-hotel-booking-dev-uk-south \
  --server sql-hotel-booking-dev-qjygtiyk \
  --name HotelBooking \
  --yes
```

The deletion is intentionally not embedded in Bicep. Incremental deployments do
not delete resources removed or renamed in a template, and an automatic delete
would remove the rollback path before the new database was verified.

## Deploy A New API Version

1. Merge the API change to `main`.
2. Wait for `Publish API image` to publish the new SHA tag.
3. Update `API_IMAGE_TAG` in the ignored `.env.azure`.
4. Reload `.env.azure`.
5. Run `what-if`.
6. Run `az deployment group create`.
7. Run `./scripts/check-azure-resources.sh`.

Bicep updates the Container App to the new immutable image. It does not require
a new Azure resource group.

## Cost Controls

- Container Apps uses Consumption with `minReplicas: 0`.
- The development database uses the Azure SQL monthly free allowance: 100,000
  vCore-seconds, 32 GB of data, and 32 GB of backup storage per month.
- Azure SQL uses serverless compute with `minCapacity: 0.5` and a 15-minute
  auto-pause delay.
- Free-limit exhaustion uses `BillOverUsage` so the interview demo remains
  available; overage is charged at normal serverless rates.
- SQL backup storage uses local redundancy.
- No Application Insights or Log Analytics resources are deployed.
- GHCR replaces a paid Azure Container Registry.

The free allowance resets monthly. Azure SQL overage and Container Apps remain
usage-based services, so check Azure Cost Management after deployment and keep a
small budget alert configured.

## Remove The Environment

Deleting the resource group permanently removes the API, SQL database, and all
hosted booking data:

```bash
az group delete \
  --name rg-hotel-booking-dev-uk-south \
  --yes \
  --no-wait
```

Run this only when the demo environment is no longer needed.
