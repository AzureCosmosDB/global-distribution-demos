using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace CosmosGlobalDistDemosCore
{
    class Program
    {

        static SingleMultiRegion singleMultiRegion;
        static SingleMultiMaster singleMultiMaster;
        static ConsistencyLatency consistencyLatency;
        static ConflictResolution conflictResolution;
        static MultiMasterFailover multiMasterFailover;

        public static async Task Main(string[] args)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile("AppSettings.json")
                .AddJsonFile("appSettings.local.json", optional: true)
                .Build();

            singleMultiRegion = new SingleMultiRegion(configuration);
            singleMultiMaster = new SingleMultiMaster(configuration);
            consistencyLatency = new ConsistencyLatency(configuration);
            conflictResolution = new ConflictResolution(configuration);
            multiMasterFailover = new MultiMasterFailover(configuration);

            //await benchmarks.RunBenchmark();
            await RunApp();
        }

        public static async Task RunApp()
        {
            bool exit = false;


            while (exit == false)
            {
                Console.Clear();
                Console.WriteLine($"Cosmos DB Global Distribution and Multi-Master Benchmarks");
                Console.WriteLine($"-----------------------------------------------------------");
                Console.WriteLine($"[1]   Single-Region vs. Multi-Region Read Latency");
                Console.WriteLine($"[2]   Read/Write Latency for Single-Master vs. Multi-Master");
                Console.WriteLine($"[3]   Consistency vs. Latency");
                Console.WriteLine($"[4]   Multi-Master Conflict Resolution");
                Console.WriteLine($"[5]   Multi-Master Failover");
                Console.WriteLine($"[6]   Initialize");
                Console.WriteLine($"[7]   How to clean up Azure resources");
                Console.WriteLine($"[8]   Exit");

                ConsoleKeyInfo result = Console.ReadKey(true);

                if (result.KeyChar == '1')
                {
                    Console.Clear();
                    await singleMultiRegion.RunBenchmarks();
                }
                else if (result.KeyChar == '2')
                {
                    Console.Clear();
                    await singleMultiMaster.RunBenchmarks();
                }
                else if (result.KeyChar == '3')
                {
                    Console.Clear();
                    await consistencyLatency.RunBenchmarks();
                }
                else if (result.KeyChar == '4')
                {
                    Console.Clear();
                    await conflictResolution.RunBenchmarks();
                }
                else if (result.KeyChar == '5')
                {
                    Console.Clear();
                    await multiMasterFailover.RunBenchmarks();
                }
                else if (result.KeyChar == '6')
                {
                    Console.WriteLine("Running Set up Routines");
                    await singleMultiRegion.Initialize();
                    await singleMultiMaster.Initialize();
                    await consistencyLatency.Initialize();
                    await conflictResolution.Initialize();
                    await multiMasterFailover.Initialize();
                }
                else if (result.KeyChar == '7')
                {
                    Console.WriteLine("The demo identity cannot delete infrastructure. To stop Cosmos DB charges, delete the dedicated demo resource group in Azure. Press any key to return.");
                    Console.ReadKey(true);
                }
                else if (result.KeyChar == '8')
                {
                    exit = true;
                }
            }
        }
    }   
}
