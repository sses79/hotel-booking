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
- One Azure SQL serverless database with 60-minute auto-pause.
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
- Azure SQL uses the serverless SKU and 60-minute auto-pause.
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
from auto-pause.

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
- Azure SQL uses serverless compute with `minCapacity: 0.5` and a 60-minute
  auto-pause delay.
- SQL backup storage uses local redundancy.
- No Application Insights or Log Analytics resources are deployed.
- GHCR replaces a paid Azure Container Registry.

Azure SQL serverless and Container Apps are usage-based services, not guaranteed
free services. Check Azure Cost Management after deployment.

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

