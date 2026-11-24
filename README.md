# Cosmos Global Distribution Demos

## Introduction

This solution contains a series of benchmarks that demonstrate various concepts for distributed databases, particularly around 
consistency and latency. The tests in this solution include:

### 1. Read latency between single region and multi-region replicated accounts

This test shows the difference in read latency for an account with a single master in East US 2 with a front end
reading from it in West US 2. The next test shows the drastic improvement in latency with data locality when the account is 
replicated to West US 2.

### 2. Write latency for accounts with Eventual consistency vs. Strong consistency + impact of distance on Strong consistency

This test shows the difference in write latency for two accounts with replicas 1000 miles apart in West US 2 and Central US regions,
one with Eventual consistency, the other with Strong consistency. There is a third test that shows the impact on latency when the 
distance between the regions is more than double the distance, demonstrating the speed of light impact on latency when using Strong 
consistency across large distances.

### 3. Read and write latency for Single-Master account versus Multi-Master account

This test shows the difference in read latency for a single-master account (master: East US 2, replica: West US 2) with a client in 
West US 2. The next test shows the impact on write latency when using a multi-master account (master: East US 2, West US 2) with 
a client in West US 2.

### 4. Multi-Master Conflict Resolution

This test generates conflicts and demonstrates conflict resolution for Last Write Wins, Custom Merge Procedure, and "Async" conflict 
resolution modes where conflicts are written to the conflicts feed and then read and resolved.

### 5. Multi-Master Failover

This drill observes SDK point reads while an operator takes a region offline on a dedicated multi-write account, then brings it back online. See [Run and reset the failover drill](#run-and-reset-the-failover-drill). The app does not modify the account or the host firewall.

## Provisioning Cosmos DB accounts

This solution requires seven different Cosmos DB accounts. Each of are configured differently to support the underlying test with 
different replication modes, consistency levels and regions.

To deploy the Cosmos DB accounts for this solution, follow the steps below.

### Steps

1. Click **Deploy to Azure**, or deploy `infra/main.bicep` using Azure CLI. Use a **dedicated resource group** and a globally unique `environmentName` prefix. The deployment creates seven accounts, their databases, containers, stored procedures and a user-assigned managed identity. Key authentication is disabled.
2. Choose the identity that will run the demos. On an Azure VM, attach the deployed identity (output `managedIdentityResourceId`) to the VM and set `AZURE_CLIENT_ID` to the `managedIdentityClientId` output before starting Visual Studio. For local development, set the optional deployment parameter `developerPrincipalId` to your user's **object ID** (`az ad signed-in-user show --query id -o tsv`) and sign in as that user with Azure CLI or Visual Studio. The template grants Cosmos DB Built-in Data Contributor on each account; allow time for the assignments to propagate.
3. In the deployment **Outputs**, find `endpoints`. Create `source/appSettings.local.json` as a JSON object using each `setting` as the property name and its full `endpoint` URI as the value. The demos use `DefaultAzureCredential`; no account keys are required.

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FAzureCosmosDB%2Fglobal-distribution-demos%2Fmain%2Finfra%2Fazuredeploy.json" target="_blank">
    <img src="http://azuredeploy.net/deploybutton.png"/>
</a>

## Provision Windows VM as Host

These tests are designed to run from a VM in West US 2 for representative latency. The failover drill does not require Windows administrator privileges.
The VM should be configured with a minimum 4 vCPU's and 16 GB of memory with RDP enabled. After the VM has been provisioned, RDP into it 
and install Visual Studio with .NET 9 support, then copy or clone the solution to the VM and configure `source/appSettings.local.json` there.

To run the demo, RDP into the VM, open the solution folder, launch the solution and press F5.

[!Note]
> You can run this demo from your local machine however the latency benchmarks will be dramatically slower and will not show actual, 
SLA-based Cosmos DB latency metrics.

[!Note]
> To change regions, edit `infra/main.bicep` and the corresponding settings in `source/AppSettings.json`. Regenerate the portal template with `bicep build infra/main.bicep --outfile infra/azuredeploy.json`.

## Run and reset the failover drill

Use only a dedicated demo account. **Offline region affects all clients and can lose writes that have not replicated.** Before starting, make sure the operator can perform both the offline and online operations on that account. These are control-plane permissions; do not grant them to the demo identity.

1. Choose **Multi-Master Failover** in the app, then **F**. Note the account endpoint and preferred read region. Wait until the app observes a read from that region before changing anything.
2. On that same account in the [Azure portal](https://learn.microsoft.com/azure/cosmos-db/how-to-manage-database-account#perform-forced-failover-for-your-azure-cosmos-db-account), open **Replicate data globally > Offline region**. Select the displayed region, review the warning, and confirm.
3. Watch for a successful read served by another region. Then bring the original region back online as described below, **even if monitoring timed out or the app exited**. Escape stops observation; it does not reset the account.

### Bring the region back online

In Azure Cloud Shell **PowerShell**, signed in as the operator, copy the demo account's **Resource ID** from its portal properties and enter the display name of the exact region you took offline (for example, `West US 2`). This calls the Cosmos DB [Online Region](https://learn.microsoft.com/rest/api/cosmos-db-resource-provider/database-accounts/online-region?view=rest-cosmos-db-resource-provider-2025-10-15) operation. The app never makes this call.

```powershell
$accountId = Read-Host 'Resource ID of the demo Cosmos DB account'
$region = Read-Host 'Region to bring back online'
Invoke-AzRestMethod -Method POST -Path "$accountId/onlineRegion?api-version=2025-10-15" `
    -Payload (@{ region = $region } | ConvertTo-Json) -WaitForCompletion -Confirm
```

Wait for completion (`202 Accepted` alone is not confirmation). Verify the region is online in **Replicate data globally**. The running app reports recovery only after a successful point read contacts the original region alone. If you restarted the app, choose **Multi-Master Failover > R** to verify recovery without offlining anything. If the operation fails or the region remains offline, retain the operation details and contact Azure Support rather than starting another drill.

## Initializing the Demos

After deployment, use **Initialize** to check access and seed empty read-benchmark partitions. Read and failover demos also seed on demand using the deployed `BulkImport` procedure. Existing data is reused.

[!IMPORTANT]
> The ten containers have 400 RU/s each, billed in each replicated region. Exiting the app does not stop charges. When finished, delete the dedicated demo resource group in Azure after checking its contents. The app's data-plane identity cannot delete infrastructure.
