using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace CosmosGlobalDistDemosCore
{
    /*
     * Resources needed for this demo:
     * 
     *   Multi-Master Failover => Cosmos DB account: Replication: Multi-Master, Read/Write Region: East US 2, West US 2, North Europe, Consistency: Eventual
     *   
    */
    class MultiMasterFailover
    {
        //Benchmarks to run
        private static List<Benchmark> benchmarks = null;

        public MultiMasterFailover(IConfiguration configuration)
        {
            try
            {
                Console.WriteLine($"Starting Multi-Master Failover");

                //Define new Benchmarks
                benchmarks = new List<Benchmark>
                {
                    new Benchmark
                    {
                        benchmarkType = BenchmarkType.Read,
                        testName = "Multi Master Failover",
                        testDescription = $"Test Reads against Multi-Master account with failover from West US 2 to East US 2",
                        testRegion = configuration["testRegion"],
                        targetRegion = configuration["multiMasterReadRegion"],
                        endpoint = configuration["MultiMasterEndpoint"],
                        writeRegion = configuration["multiMasterWriteRegion"],
                        readRegion = configuration["multiMasterReadRegion"],
                        databaseId = configuration["databaseId"],
                        containerId = configuration["containerId"],
                        partitionKeyValue = configuration["partitionKeyValue"]
                    }
                };

                foreach (Benchmark benchmark in benchmarks)
                {
                    benchmark.client = Helpers.CreateClient(benchmark.endpoint, benchmark.targetRegion);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + "\nPress any key to continue");
                Console.ReadKey();
            }
        }

        public async Task Initialize()
        {
            try
            {
                Console.WriteLine("Multi Master Failover Initialize");

                foreach (Benchmark benchmark in benchmarks)
                {
                    await Benchmark.InitializeBenchmark(benchmark);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + "\nPress any key to continue");
                Console.ReadKey();
            }
        }
        public async Task RunBenchmarks()
        {
            try
            {
                Console.WriteLine($"Test reads from Multi-Master account during failover\n{Helpers.Line}\nPlease Wait...");

                //Run benchmarks, collect results
                Benchmark benchmark = benchmarks[0];

                List<string> ids = await Benchmark.GetOrSeedIds(benchmark);
                string id = ids[0];

                int seconds = 0;

                ItemResponse<SampleCustomer> response = await benchmark.container.ReadItemAsync<SampleCustomer>(id: id, partitionKey: new PartitionKey(benchmark.partitionKeyValue));
                var initialContacts = response.Diagnostics.GetContactedRegions();
                if (initialContacts.Count != 1)
                    throw new InvalidOperationException("The initial read must contact exactly one region before simulating failover.");
                var (initialRegion, regionalEndpoint) = initialContacts[0];
                string ipAddress = Dns.GetHostAddresses(regionalEndpoint.Host)[0].ToString();

                Console.WriteLine($"Initiate Reads from {initialRegion}");

                Console.WriteLine($"Simulating regional failure by creating a Firewall Rule blocking the ip address for {initialRegion}");
                Firewall.AddFirewallRule("Failover Test", ipAddress);

                try
                {
                    while (true)
                    {
                        response = await benchmark.container.ReadItemAsync<SampleCustomer>(id: id, partitionKey: new PartitionKey(benchmark.partitionKeyValue));
                        var regions = response.Diagnostics.GetContactedRegions().Select(contact => contact.regionName).ToList();
                        Console.WriteLine($"Regions contacted: {string.Join(", ", regions)}");

                        if (regions.Count > 0 && !regions.Contains(initialRegion))
                        {
                            Console.WriteLine($"Region failover has occurred. Complete in {seconds} seconds\nPress any key to continue\n...");
                            Console.ReadKey(true);
                            break;
                        }

                        await Task.Delay(1000);
                        seconds++;
                    }
                }
                finally
                {
                    Firewall.RemoveFirewallRule("Failover Test");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + "\nPress any key to continue");
                Console.ReadKey();
            }
        }
    }
}
