using '../main.bicep'

param location = 'uksouth'
param environmentName = 'dev'
param projectName = 'hotel-booking'
param apiImageTag = readEnvironmentVariable('API_IMAGE_TAG')
param sqlAdministratorPassword = readEnvironmentVariable('AZURE_SQL_ADMIN_PASSWORD')
