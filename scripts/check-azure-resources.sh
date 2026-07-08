#!/usr/bin/env bash

set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-hotel-booking-dev-uk-south}"
DEPLOYMENT_NAME="${DEPLOYMENT_NAME:-main}"

for command_name in az curl; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  fi
done

if [[ "$(az group exists --name "$RESOURCE_GROUP")" != "true" ]]; then
  printf 'Azure resource group not found: %s\n' "$RESOURCE_GROUP" >&2
  exit 1
fi

deployment_count="$(az deployment group list \
  --resource-group "$RESOURCE_GROUP" \
  --query "length([?name=='$DEPLOYMENT_NAME'])" \
  --output tsv)"

if [[ "$deployment_count" != "1" ]]; then
  printf 'Azure deployment not found: %s in %s\n' \
    "$DEPLOYMENT_NAME" \
    "$RESOURCE_GROUP" >&2
  exit 1
fi

deployment_state="$(az deployment group show \
  --name "$DEPLOYMENT_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.provisioningState \
  --output tsv)"

api_url="$(az deployment group show \
  --name "$DEPLOYMENT_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.outputs.apiUrl.value \
  --output tsv)"

api_app_name="$(az deployment group show \
  --name "$DEPLOYMENT_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.outputs.apiAppName.value \
  --output tsv)"

sql_server_name="$(az deployment group show \
  --name "$DEPLOYMENT_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.outputs.sqlServerName.value \
  --output tsv)"

sql_database_name="$(az deployment group show \
  --name "$DEPLOYMENT_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.outputs.sqlDatabaseName.value \
  --output tsv)"

api_resource="$(az resource show \
  --name "$api_app_name" \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.App/containerApps \
  --api-version 2024-03-01 \
  --output json)"

api_state="$(az resource show \
  --name "$api_app_name" \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.App/containerApps \
  --api-version 2024-03-01 \
  --query properties.provisioningState \
  --output tsv)"

api_image="$(az resource show \
  --name "$api_app_name" \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.App/containerApps \
  --api-version 2024-03-01 \
  --query 'properties.template.containers[0].image' \
  --output tsv)"

min_replicas="$(az resource show \
  --name "$api_app_name" \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.App/containerApps \
  --api-version 2024-03-01 \
  --query properties.template.scale.minReplicas \
  --output tsv)"

max_replicas="$(az resource show \
  --name "$api_app_name" \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.App/containerApps \
  --api-version 2024-03-01 \
  --query properties.template.scale.maxReplicas \
  --output tsv)"

environment_id="$(az resource show \
  --name "$api_app_name" \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.App/containerApps \
  --api-version 2024-03-01 \
  --query properties.managedEnvironmentId \
  --output tsv)"
environment_name="${environment_id##*/}"

log_destination="$(az resource show \
  --name "$environment_name" \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.App/managedEnvironments \
  --api-version 2024-03-01 \
  --query properties.appLogsConfiguration.destination \
  --output tsv)"

sql_state="$(az sql db show \
  --name "$sql_database_name" \
  --server "$sql_server_name" \
  --resource-group "$RESOURCE_GROUP" \
  --query status \
  --output tsv)"

sql_sku="$(az sql db show \
  --name "$sql_database_name" \
  --server "$sql_server_name" \
  --resource-group "$RESOURCE_GROUP" \
  --query sku.name \
  --output tsv)"

sql_auto_pause="$(az sql db show \
  --name "$sql_database_name" \
  --server "$sql_server_name" \
  --resource-group "$RESOURCE_GROUP" \
  --query autoPauseDelay \
  --output tsv)"

application_insights_count="$(az resource list \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.Insights/components \
  --query 'length(@)' \
  --output tsv)"

log_analytics_count="$(az resource list \
  --resource-group "$RESOURCE_GROUP" \
  --resource-type Microsoft.OperationalInsights/workspaces \
  --query 'length(@)' \
  --output tsv)"

health_response="$(curl \
  --fail \
  --silent \
  --show-error \
  --retry 6 \
  --retry-delay 5 \
  --retry-all-errors \
  --max-time 60 \
  "${api_url}/health")"

[[ -n "$api_resource" ]]
[[ "$deployment_state" == "Succeeded" ]]
[[ "$api_state" == "Succeeded" ]]
[[ "$api_image" == ghcr.io/sses79/hotel-booking-api:* ]]
[[ "$api_image" != *":latest" ]]
[[ "$min_replicas" == "0" ]]
[[ "$max_replicas" == "2" ]]
[[ -z "$log_destination" ]]
[[ "$sql_state" == "Online" || "$sql_state" == "Paused" ]]
[[ "$sql_sku" == "GP_S_Gen5" ]]
[[ "$sql_auto_pause" == "60" ]]
[[ "$application_insights_count" == "0" ]]
[[ "$log_analytics_count" == "0" ]]
[[ "$health_response" == "Healthy" ]]

printf '%s\n' \
  "Azure resource checks passed:" \
  "  Resource group: $RESOURCE_GROUP" \
  "  API: $api_url" \
  "  Image: $api_image" \
  "  Scale: $min_replicas-$max_replicas replicas" \
  "  Azure SQL: $sql_database_name is $sql_state ($sql_sku, ${sql_auto_pause}-minute auto-pause)" \
  "  Monitoring: no Application Insights or Log Analytics" \
  "  Health: $health_response"
