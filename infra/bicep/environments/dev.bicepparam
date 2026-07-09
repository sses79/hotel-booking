using '../main.bicep'

param location = 'uksouth'
param environmentName = 'dev'
param projectName = 'hotel-booking'
param apiImageTag = readEnvironmentVariable('API_IMAGE_TAG')
param sqlAdministratorPassword = readEnvironmentVariable('AZURE_SQL_ADMIN_PASSWORD')
param sqlEntraAdministratorLogin = 'sses79_hotmail.com#EXT#@sses79hotmail.onmicrosoft.com'
param sqlEntraAdministratorObjectId = 'da629604-9a9f-45c3-b6d5-8240fc2b7705'
