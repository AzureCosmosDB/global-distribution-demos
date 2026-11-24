targetScope = 'resourceGroup'

@minLength(1)
@maxLength(22)
@description('Globally unique account name prefix (letters, digits and hyphens).')
param environmentName string

@description('Optional Entra object ID for a local developer, in addition to the demo VM identity.')
param developerPrincipalId string = ''

var accounts = [
  {
    suffix: 'single-master'
    setting: 'SingleMasterEndpoint'
    consistency: 'Session'
    multiWrite: false
    regions: [ 'East US 2', 'West US 2' ]
  }
  {
    suffix: 'multi-master'
    setting: 'MultiMasterEndpoint'
    consistency: 'Session'
    multiWrite: true
    regions: [ 'West US 2', 'East US 2', 'North Europe' ]
  }
  {
    suffix: 'consistency-eventual'
    setting: 'EventualEndpoint'
    consistency: 'Eventual'
    multiWrite: true
    regions: [ 'West US 2', 'Central US' ]
  }
  {
    suffix: 'consistency-strong'
    setting: 'Strong1kMilesEndpoint'
    consistency: 'Strong'
    multiWrite: false
    regions: [ 'West US 2', 'Central US' ]
  }
  {
    suffix: 'consistency-strong2'
    setting: 'Strong2kMilesEndpoint'
    consistency: 'Strong'
    multiWrite: false
    regions: [ 'West US 2', 'East US 2' ]
  }
  {
    suffix: 'latency-single-region'
    setting: 'singleRegionEndpoint'
    consistency: 'Eventual'
    multiWrite: false
    regions: [ 'East US 2' ]
  }
  {
    suffix: 'latency-multi-region'
    setting: 'multiRegionEndpoint'
    consistency: 'Eventual'
    multiWrite: false
    regions: [ 'East US 2', 'West US 2' ]
  }
]

resource databaseAccounts 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = [for account in accounts: {
  name: toLower('${environmentName}-${account.suffix}')
  location: 'West US 2'
  kind: 'GlobalDocumentDB'
  properties: {
    publicNetworkAccess: 'Enabled'
    enableMultipleWriteLocations: account.multiWrite
    databaseAccountOfferType: 'Standard'
    disableLocalAuth: true
    enablePerRegionPerPartitionAutoscale: true
    consistencyPolicy: {
      defaultConsistencyLevel: account.consistency
    }
    locations: [for (region, priority) in account.regions: {
      locationName: region
      failoverPriority: priority
      isZoneRedundant: false
    }]
  }
}]

resource uaIdentityResource 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: toLower('${environmentName}-mi')
  location: 'West US 2'
}

var dataContributorRole = '00000000-0000-0000-0000-000000000002'

resource managedIdentityDataContributors 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = [for (_, i) in accounts: {
  parent: databaseAccounts[i]
  name: guid(databaseAccounts[i].id, uaIdentityResource.id, dataContributorRole)
  properties: {
    roleDefinitionId: '${databaseAccounts[i].id}/sqlRoleDefinitions/${dataContributorRole}'
    principalId: uaIdentityResource.properties.principalId
    scope: databaseAccounts[i].id
  }
}]

resource developerDataContributors 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = [for (_, i) in accounts: if (!empty(developerPrincipalId)) {
  parent: databaseAccounts[i]
  name: guid(databaseAccounts[i].id, developerPrincipalId, dataContributorRole)
  properties: {
    roleDefinitionId: '${databaseAccounts[i].id}/sqlRoleDefinitions/${dataContributorRole}'
    principalId: developerPrincipalId
    scope: databaseAccounts[i].id
  }
}]

resource databases 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = [for (_, i) in accounts: {
  parent: databaseAccounts[i]
  name: 'demo'
  properties: {
    resource: { id: 'demo' }
  }
}]

var partitionKey = {
  paths: [ '/myPartitionKey' ]
  kind: 'Hash'
}
var throughput = { throughput: 400 }

resource customers 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [for (_, i) in accounts: {
  parent: databases[i]
  name: 'customers'
  properties: {
    resource: {
      id: 'customers'
      partitionKey: partitionKey
    }
    options: throughput
  }
}]

resource bulkImports 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/storedProcedures@2024-11-15' = [for (_, i) in accounts: {
  parent: customers[i]
  name: 'BulkImport'
  properties: {
    resource: {
      id: 'BulkImport'
      body: loadTextContent('../source/spBulkUpload.js')
    }
  }
}]

var multiMasterIndex = indexOf(map(accounts, account => account.suffix), 'multi-master')
var mergeContainer = 'customersUdp'
var conflictContainers = [
  {
    name: 'customersLww'
    policy: { mode: 'LastWriterWins', conflictResolutionPath: '/userDefinedId' }
  }
  {
    name: mergeContainer
    policy: { mode: 'Custom', conflictResolutionProcedure: 'dbs/demo/colls/${mergeContainer}/sprocs/MergeProcedure' }
  }
  {
    name: 'customersNone'
    policy: { mode: 'Custom' }
  }
]

resource conflicts 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = [for container in conflictContainers: {
  parent: databases[multiMasterIndex]
  name: container.name
  properties: {
    resource: {
      id: container.name
      partitionKey: partitionKey
      conflictResolutionPolicy: container.policy
    }
    options: throughput
  }
}]

resource mergeProcedure 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers/storedProcedures@2024-11-15' = {
  parent: conflicts[indexOf(map(conflictContainers, container => container.name), mergeContainer)]
  name: 'MergeProcedure'
  properties: {
    resource: {
      id: 'MergeProcedure'
      body: loadTextContent('../source/spConflictUDP.js')
    }
  }
}

output managedIdentityResourceId string = uaIdentityResource.id
output managedIdentityClientId string = uaIdentityResource.properties.clientId
output endpoints array = [for (account, i) in accounts: {
  setting: account.setting
  endpoint: databaseAccounts[i].properties.documentEndpoint
}]
