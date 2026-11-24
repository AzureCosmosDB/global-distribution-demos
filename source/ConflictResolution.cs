using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CosmosGlobalDistDemosCore
{
    /*
     * Resources needed for this demo:
     * 
     *   Shared with SingleMultiMaster.cs
     *   Multi-Master => Cosmos DB account: Replication: Multi-Master, Write Region: East US 2, West US 2, North Europe, Consistency: Session
     *   
    */
    class ConflictResolution
    {
        //private static List<ConflictGenerator> conflicts = null;
        private static ConflictGenerator lww;
        private static ConflictGenerator custom;
        private static ConflictGenerator none;

        public ConflictResolution(IConfiguration configuration)
        {
            try
            {
                Console.WriteLine("Starting Conflict Resolution");

                lww = new ConflictGenerator
                {
                    testName = "Generate insert conflicts on container with Last Writer Wins Policy (Max UserDefinedId Wins).",
                    endpoint = configuration["MultiMasterEndpoint"],
                    databaseId = configuration["databaseId"],
                    containerId = configuration["LwwPolicyContainer"],
                    partitionKeyValue = configuration["partitionKeyValue"],
                    replicaRegions = new List<ReplicaRegion>()
                };

                custom = new ConflictGenerator
                {
                    testName = "Generate insert conflicts on container with User Defined Procedure Policy (Min UserDefinedId Wins).",
                    endpoint = configuration["MultiMasterEndpoint"],
                    databaseId = configuration["databaseId"],
                    containerId = configuration["UdpPolicyContainer"],
                    partitionKeyValue = configuration["partitionKeyValue"],
                    replicaRegions = new List<ReplicaRegion>()
                };

                none = new ConflictGenerator
                {
                    testName = "Generate update conficts on container with no Policy defined, write to Conflicts Feed.",
                    endpoint = configuration["MultiMasterEndpoint"],
                    databaseId = configuration["databaseId"],
                    containerId = configuration["NoPolicyContainer"],
                    partitionKeyValue = configuration["partitionKeyValue"],
                    replicaRegions = new List<ReplicaRegion>()
                };
                
                //Load the regions and initialize the clients
                List<string> regions = configuration["ConflictRegions"].Split(new char[] { ';' }).ToList();
                foreach (string region in regions)
                {
                    lww.replicaRegions.Add(new ReplicaRegion
                    {
                        region = region,
                        client = Helpers.CreateClient(lww.endpoint, region)
                    });

                    custom.replicaRegions.Add(new ReplicaRegion
                    {
                        region = region,
                        client = Helpers.CreateClient(custom.endpoint, region)
                    });

                    none.replicaRegions.Add(new ReplicaRegion
                    {
                        region = region,
                        client = Helpers.CreateClient(none.endpoint, region)
                    });
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
                Console.WriteLine("MultiMaster Conflicts Initialize");

                await lww.InitializeConflicts(lww);
                await custom.InitializeConflicts(custom);
                await none.InitializeConflicts(none);

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
                bool exit = false;

                while (exit == false)
                {
                    Console.Clear();
                    Console.WriteLine($"Multi Master Conflict Resolution\n{Helpers.Line}\n");
                    Console.WriteLine($"[1]   Last Writer Wins");
                    Console.WriteLine($"[2]   Custom Merge Stored Procedure");
                    Console.WriteLine($"[3]   Conflict Feed Generate");
                    Console.WriteLine($"[4]   Conflict Conflict Resolution");
                    Console.WriteLine($"[5]   Return");

                    ConsoleKeyInfo result = Console.ReadKey(true);

                    if (result.KeyChar == '1')
                    {
                        Console.Clear();
                        await lww.GenerateInsertConflicts(lww);
                    }
                    else if (result.KeyChar == '2')
                    {
                        Console.Clear();
                        await custom.GenerateInsertConflicts(custom);
                    }
                    else if (result.KeyChar == '3')
                    {
                        Console.Clear();
                        await none.GenerateUpdateConflicts(none);
                    }
                    else if (result.KeyChar == '4')
                    {
                        Console.Clear();
                        await none.ProcessConflicts(none);
                    }
                    else if (result.KeyChar == '5')
                    {
                        exit = true;
                    }
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
