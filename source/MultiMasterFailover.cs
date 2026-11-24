using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace CosmosGlobalDistDemosCore
{
    class MultiMasterFailover
    {
        private readonly Benchmark benchmark;

        public MultiMasterFailover(IConfiguration configuration)
        {
            benchmark = new Benchmark
            {
                benchmarkType = BenchmarkType.Read,
                targetRegion = configuration["multiMasterReadRegion"],
                endpoint = configuration["MultiMasterEndpoint"],
                databaseId = configuration["databaseId"],
                containerId = configuration["containerId"],
                partitionKeyValue = configuration["partitionKeyValue"]
            };
        }

        private async Task<string> GetSampleIdAsync()
        {
            if (string.IsNullOrWhiteSpace(benchmark.targetRegion))
                throw new InvalidOperationException("Set multiMasterReadRegion before starting the failover drill.");
            benchmark.client ??= Helpers.CreateClient(benchmark.endpoint, benchmark.targetRegion);
            return (await Benchmark.GetOrSeedIds(benchmark))[0];
        }

        public async Task Initialize()
        {
            try
            {
                Console.WriteLine("Multi Master Failover Initialize");

                await GetSampleIdAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + "\nPress any key to continue");
                Console.ReadKey();
            }
        }
        public async Task RunBenchmarks()
        {
            string original = benchmark.targetRegion;
            Console.WriteLine($"Demo account: {benchmark.endpoint}\nPreferred read region: {original}");
            Console.WriteLine("Use only a dedicated demo account. Taking a region offline affects all clients and can lose unreplicated writes.");
            Console.WriteLine("Reset: use the README's 'Bring the region back online' command for this account and region, even if monitoring stops.");
            Console.WriteLine("[F] Observe failover and recovery  [R] Verify recovery only  [Any other key] Return");
            var choice = Console.ReadKey(true).Key;
            if (choice != ConsoleKey.F && choice != ConsoleKey.R)
                return;

            try
            {
                string id = await GetSampleIdAsync();
                if (choice == ConsoleKey.F)
                {
                    Console.WriteLine($"Checking that reads are served by {original} before the drill.");
                    await WaitForReadAsync(id, inPreferredRegion: true);
                    Console.WriteLine($"In the demo account's Azure portal, select Replicate data globally > Offline region > {original}.");
                    string alternate = await WaitForReadAsync(id, inPreferredRegion: false);
                    Console.WriteLine($"Failover observed: a point read was served by {alternate}.");
                }

                Console.WriteLine($"Run the README's 'Bring the region back online' command for {original} if you haven't already.");
                await WaitForReadAsync(id, inPreferredRegion: true);
                Console.WriteLine($"Recovery verified: a point read was served by {original}.");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            Console.WriteLine("Monitoring does not change region status. Press any key to return.");
            Console.ReadKey(true);
        }

        internal static string MatchReadRegion(IReadOnlyList<string> regions, string preferredRegion, bool inPreferredRegion)
        {
            // Diagnostics do not order retries. Only a single-region read identifies the serving region.
            if (regions.Count != 1 || string.IsNullOrWhiteSpace(regions[0]))
                return null;
            string region = regions[0];
            bool preferred = string.Equals(region.Replace(" ", ""), preferredRegion.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
            return preferred == inPreferredRegion ? region : null;
        }

        private async Task<string> WaitForReadAsync(string id, bool inPreferredRegion)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            Console.WriteLine("Watching reads for up to 10 minutes. Press Escape to stop monitoring.");
            try
            {
                while (true)
                {
                    if (!Console.IsInputRedirected && Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                        throw new OperationCanceledException("Monitoring stopped by operator.");
                    try
                    {
                        var read = await benchmark.container.ReadItemAsync<SampleCustomer>(id, new PartitionKey(benchmark.partitionKeyValue),
                            cancellationToken: timeout.Token);
                        var regions = read.Diagnostics.GetContactedRegions().Select(c => c.regionName).ToArray();
                        Console.WriteLine($"Successful read; regions contacted: {string.Join(", ", regions)}");
                        string match = MatchReadRegion(regions, benchmark.targetRegion, inPreferredRegion);
                        if (match != null)
                            return match;
                    }

                    catch (CosmosException e) when (e.StatusCode == HttpStatusCode.ServiceUnavailable || e.StatusCode == HttpStatusCode.RequestTimeout || e.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine($"Read returned {(int)e.StatusCode}; waiting for a successful read.");
                    }
                    await Task.Delay(1000, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("No matching read observed within 10 minutes. Check region status in the portal; use recovery-only mode to resume.");
            }
        }
    }
}
