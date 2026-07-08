# Azure Bicep Guide

This guide explains the Azure and Bicep principles worth carrying from
`/Users/tim/Ware/tfl-analytics` into the hotel booking API challenge.

The 80/20 rule here: a useful Azure submission does not need a huge platform.
It needs a repeatable deployment, clear parameters, safe defaults, managed
secrets, and a short verification path.

## What To Learn From tfl-analytics

The `tfl-analytics` project uses a strong Azure pattern:

- Infrastructure is declared in `infra/bicep`.
- `main.bicep` composes small modules.
- Environment values live in `.bicepparam` files.
- Resource names are generated consistently with a unique suffix.
- Optional resources are controlled by boolean parameters.
- App settings are deployed with infrastructure.
- Managed identities and RBAC are preferred over raw secrets.
- Deployment is always compile, preview, validate, deploy, verify.
- Smoke tests and runbooks explain how to prove the environment works.

For the hotel booking challenge, keep the same discipline but use fewer
resources.

## Small Azure Architecture For This Challenge

Recommended minimal Azure shape:

```text
Browser / Swagger
  -> ASP.NET Core API
  -> Azure SQL Database
  -> Application Insights optional
```

Good hosting options:

- Azure App Service: straightforward for a small ASP.NET Core API.
- Azure Container Apps: good if the API already has a Dockerfile and you want
  scale-to-zero behavior.

For this challenge, App Service is usually simpler. Container Apps is fine if
the repository already has container build/deploy steps.

Use Azure SQL if hosting in Azure. Use SQLite locally if that keeps development
fast. EF Core can support both through configuration.

## Suggested Bicep Layout

```text
infra/
  bicep/
    main.bicep
    environments/
      dev.bicepparam
    modules/
      app-service.bicep
      sql.bicep
      key-vault.bicep
      observability.bicep
```

Start smaller if needed:

```text
infra/
  bicep/
    main.bicep
    dev.bicepparam
```

Use modules when a file becomes hard to scan, or when the resource group has
clear building blocks such as hosting, database, secrets, and monitoring.

## Main Bicep Principles

Use `main.bicep` as the composition root:

```bicep
targetScope = 'resourceGroup'

param location string = resourceGroup().location
param environmentName string = 'dev'
param projectName string = 'hotel-booking'
param enableObservability bool = false

var suffix = take(uniqueString(subscription().id, resourceGroup().id), 8)
var commonTags = {
  project: projectName
  environment: environmentName
  managedBy: 'bicep'
}
```

Core ideas:

- Parameters describe what changes per environment.
- Variables derive repeatable names.
- Tags make resources searchable.
- Optional modules reduce cost while preserving a path to production.
- Outputs expose useful names and hostnames for scripts and smoke tests.

## Resource Naming

Use predictable names with a unique suffix:

```bicep
var appServicePlanName = 'plan-${projectName}-${environmentName}-${suffix}'
var webAppName = 'app-${projectName}-${environmentName}-${suffix}'
var sqlServerName = 'sql-${projectName}-${environmentName}-${suffix}'
var keyVaultName = 'kv-hotel-${suffix}'
```

Names should be boring. Boring names are easier to debug.

## App Hosting

For App Service:

- Use Linux.
- Enable HTTPS only.
- Set minimum TLS version.
- Put connection strings and config in app settings.
- Add a `/health` endpoint in the API.

For Container Apps:

- Use an immutable image tag, ideally a commit SHA.
- Set CPU and memory deliberately.
- Add a liveness probe.
- Scale low for development.

The useful `tfl-analytics` idea is not the exact service choice. It is that the
API host, environment variables, identity, health probe, and outputs are all
declared together.

## Database

For a hosted challenge, Azure SQL is the most natural EF Core target.

Minimum design:

- SQL server.
- SQL database.
- Firewall or private access decision.
- Connection string passed to the API.
- EF Core migrations run during deployment or documented as a manual step.

For a challenge, it is acceptable to document:

```bash
dotnet ef database update --project src/HotelBooking.Services --startup-project src/HotelBooking.Api
```

If using Azure SQL, avoid printing passwords or connection strings in logs.

## Secrets

Use Key Vault for secrets when the deployment has anything sensitive.

For this hotel challenge, possible secrets are:

- SQL administrator password.
- Application Insights connection string if not exposed as a normal app setting.
- Any future external API key.

The `tfl-analytics` pattern to copy:

- Create Key Vault with RBAC authorization.
- Give the app identity only the role it needs.
- Use app settings to reference Key Vault secrets.
- Never write secret values into documentation or command output.

## Managed Identity And RBAC

Prefer this mental model:

```text
The app has an identity.
The identity receives the smallest role needed.
The app uses that identity instead of stored credentials where possible.
```

For a small App Service plus Azure SQL deployment, password auth may be quicker.
For a stronger production-style submission, use managed identity for Azure
resources and document the choice.

## Environment Parameters

Keep environment-specific values in `.bicepparam` files:

```bicep
using '../main.bicep'

param location = 'uksouth'
param environmentName = 'dev'
param projectName = 'hotel-booking'
param enableObservability = false
```

The parameter file should be safe to commit. Do not put secret values in it.

## Deployment Commands

Run from the repository root:

```bash
az login
az account set --subscription "<subscription-name-or-id>"
```

Create or confirm a resource group:

```bash
az group create \
  --name rg-hotel-booking-dev-uk-south \
  --location uksouth
```

Compile Bicep:

```bash
az bicep build --file infra/bicep/main.bicep
```

Preview changes:

```bash
az deployment group what-if \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam
```

Validate:

```bash
az deployment group validate \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam
```

Deploy:

```bash
az deployment group create \
  --name hotel-booking-dev \
  --resource-group rg-hotel-booking-dev-uk-south \
  --template-file infra/bicep/main.bicep \
  --parameters infra/bicep/environments/dev.bicepparam \
  --output table
```

## Review The What-If Output

Stop before deploying if `what-if` shows:

- An unexpected delete.
- A paid SKU you did not intend.
- Replacement of a database.
- Wider public access than expected.
- Reduced retention or security controls.

This is one of the biggest lessons from `tfl-analytics`: preview first, then
deploy with intent.

## Application Deployment

For App Service source or zip deployment:

```bash
dotnet publish src/HotelBooking.Api/HotelBooking.Api.csproj -c Release
```

Then deploy using your chosen App Service method.

For container deployment:

```bash
docker build \
  --file src/HotelBooking.Api/Dockerfile \
  --tag hotel-booking-api:<commit-sha> .
```

Prefer immutable tags for deployed images. Avoid deploying `latest` when you
want a repeatable review.

## Post-Deployment Verification

Create a small checklist:

```bash
curl --fail https://<api-host>/health
curl --fail https://<api-host>/swagger/index.html
```

Then verify the actual business flow:

1. Reset data.
2. Seed data.
3. Search for a hotel by name.
4. Search available rooms.
5. Create a booking.
6. Look up the booking by reference.
7. Try an overlapping booking and confirm it is rejected or another room is
   chosen correctly.

This is better than only proving that Azure resources exist.

## Cost Control

Keep cost visible:

- Use development-sized SKUs.
- Make observability optional if log ingestion cost is a concern.
- Delete unused resources.
- Avoid over-provisioned databases.
- Document any free-tier assumptions and check current Azure pricing before
  leaving resources running.

The guide should not promise a resource is free forever. Cloud pricing changes.

## What To Include In The Challenge Repo

For a strong submission, include:

- `infra/bicep` with a minimal deployable template.
- `docs/Azure-bicep-guide.md`.
- A README section explaining Azure deployment.
- A post-deployment checklist.
- A note saying Azure hosting is optional per the brief, but supported.

## Future Codex Notes

When implementing Azure for this hotel API later:

- Start with local correctness first.
- Add Bicep after the API and tests are stable.
- Keep the first Azure version minimal.
- Use `az bicep build`, `what-if`, and `validate` before `create`.
- Never print secrets.
- Verify the real booking workflow after deployment.
