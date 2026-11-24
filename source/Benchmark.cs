using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;

namespace CosmosGlobalDistDemosCore
{
    public enum BenchmarkType
    {
        Write = 0,
        Read = 1
    }

    public class Benchmark
    {
        private const int ReadSampleSize = 100;

        public BenchmarkType benchmarkType;
        public string testName;
        public string testDescription;
        public string testRegion;
        public string targetRegion;
        public CosmosClient client;
        public Container container;
        public string endpoint;
        public string readRegion;
        public string writeRegion;
        public string databaseId;
        public string containerId;
        public string partitionKeyValue;
        public ResultSummary resultSummary;

        public static async Task InitializeBenchmark(Benchmark benchmark)
        {
            if (benchmark.benchmarkType == BenchmarkType.Read)
            {
                await GetOrSeedIds(benchmark);
            }
            else
            {
                benchmark.container ??= benchmark.client.GetContainer(benchmark.databaseId, benchmark.containerId);
                await benchmark.container.ReadContainerAsync();
            }
        }

        private static async Task LoadData(Benchmark benchmark, int count)
        {
            Console.WriteLine("Pre-loading data for benchmark...");
            List<SampleCustomer> customers = SampleCustomer.GenerateCustomers(benchmark.partitionKeyValue, count);
            int inserted = 0;

            while (inserted < customers.Count)
            {
                dynamic[] args = { customers.Skip(inserted).ToArray() };
                StoredProcedureExecuteResponse<int> result = await benchmark.container.Scripts.ExecuteStoredProcedureAsync<int>(
                    "BulkImport", new PartitionKey(benchmark.partitionKeyValue), args);
                if (result.Resource <= 0)
                    throw new InvalidOperationException("BulkImport inserted no items. Check container throughput before retrying.");
                inserted += result.Resource;
                Console.WriteLine($"Inserted {inserted} items.");
            }
        }
        public static async Task ReadBenchmark(Benchmark benchmark)
        {
            //Fetch the id values to do point reads on
            List<string> ids = await GetOrSeedIds(benchmark);

            //Console.WriteLine($"\nTest {ids.Count} {GetBenchmarkType(benchmark)} against {benchmark.testName} account in {benchmark.readRegion} from {benchmark.testRegion}\nPress any key to continue\n...");
            Console.WriteLine($"\n{benchmark.testDescription}\nPress any key to continue\n...");
            Console.ReadKey(true);

            int test = 0;
            List<Result> results = new List<Result>(); //Individual Benchmark results
            Stopwatch stopwatch = new Stopwatch();

            foreach (string id in ids)
            {
                stopwatch.Start();
                    ItemResponse<SampleCustomer> response = await benchmark.container.ReadItemAsync<SampleCustomer>(id: id, partitionKey: new PartitionKey(benchmark.partitionKeyValue));
                stopwatch.Stop();

                Console.WriteLine($"Read {test++} of {ids.Count}, region: {benchmark.readRegion}, Latency: {stopwatch.ElapsedMilliseconds} ms, Request Charge: {response.RequestCharge} RUs");

                results.Add(new Result(stopwatch.ElapsedMilliseconds, response.RequestCharge));

                stopwatch.Reset();
            }

            OutputResults(benchmark, results);
        }
        public static async Task WriteBenchmark(Benchmark benchmark)
        {
            //Verify the benchmark is setup
            await InitializeBenchmark(benchmark);

            //Customers to insert
            List<SampleCustomer> customers = SampleCustomer.GenerateCustomers(benchmark.partitionKeyValue, 100);

            //Console.WriteLine($"\nTest {customers.Count} {GetBenchmarkType(benchmark)} against {benchmark.testName} account in {benchmark.writeRegion} from {benchmark.testRegion}\nPress any key to continue\n...");
            Console.WriteLine($"\n{benchmark.testDescription}\nPress any key to continue\n...");
            Console.ReadKey(true);

            int test = 0;
            List<Result> results = new List<Result>(); //Individual Benchmark results
            Stopwatch stopwatch = new Stopwatch();

            foreach (SampleCustomer customer in customers)
            {
                stopwatch.Start();
                    ItemResponse<SampleCustomer> response = await benchmark.container.CreateItemAsync<SampleCustomer>(customer, new PartitionKey(benchmark.partitionKeyValue));
                stopwatch.Stop();

                Console.WriteLine($"Write {test++} of {customers.Count}, region: {benchmark.writeRegion}, Latency: {stopwatch.ElapsedMilliseconds} ms, Request Charge: {response.RequestCharge} RUs");

                results.Add(new Result(stopwatch.ElapsedMilliseconds, response.RequestCharge));

                stopwatch.Reset();
            }

            OutputResults(benchmark, results);
        }
        public static Task<List<string>> GetOrSeedIds(Benchmark benchmark) => GetOrSeedIds(benchmark, ReadSampleSize);

        internal static async Task<List<string>> GetOrSeedIds(Benchmark benchmark, int requiredCount, TimeSpan? visibilityTimeout = null)
        {
            benchmark.container ??= benchmark.client.GetContainer(benchmark.databaseId, benchmark.containerId);
            List<string> ids = await GetIds(benchmark, requiredCount);
            if (ids.Count == requiredCount)
                return ids;

            if (ids.Count == 0)
                await LoadData(benchmark, requiredCount);

            using var timeout = new CancellationTokenSource(visibilityTimeout ?? TimeSpan.FromSeconds(30));
            try
            {
                while (true)
                {
                    ids = await GetIds(benchmark, requiredCount, timeout.Token);
                    if (ids.Count == requiredCount)
                        return ids;
                    await Task.Delay(250, timeout.Token);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Only {ids.Count} of {requiredCount} required IDs were visible for partition {benchmark.partitionKeyValue} (preferred region: {benchmark.targetRegion}). The test did not start.");
            }
        }

        private static async Task<List<string>> GetIds(Benchmark benchmark, int requiredCount, CancellationToken cancellationToken = default)
        {
            var results = new HashSet<string>();
            QueryDefinition query = new QueryDefinition("SELECT TOP @count VALUE c.id FROM c").WithParameter("@count", requiredCount);

            using FeedIterator<string> resultSetIterator = benchmark.container.GetItemQueryIterator<string>(
                query, requestOptions: new QueryRequestOptions() { PartitionKey = new PartitionKey(benchmark.partitionKeyValue) });

            while (resultSetIterator.HasMoreResults)
            {
                FeedResponse<string> response = await resultSetIterator.ReadNextAsync(cancellationToken);
                results.UnionWith(response);
            }

            return results.ToList();
        }
        private static string GetBenchmarkType(Benchmark benchmark)
        {
            string benchmarkType = string.Empty;

            switch (benchmark.benchmarkType)
            {
                case BenchmarkType.Read:
                    benchmarkType = "Reads";
                    break;
                case BenchmarkType.Write:
                    benchmarkType = "Writes";
                    break;
            }
            return benchmarkType;
        }
        private static void OutputResults(Benchmark benchmark, List<Result> results)
        {
            string benchmarkType = GetBenchmarkType(benchmark);
            string testName = benchmark.testName;
            string testRegion = benchmark.testRegion;
            string toRegion = benchmark.writeRegion;
            string targetRegion = benchmark.targetRegion;

            //Average at 99th Percentile
            string averageLatency = Math.Round(results.OrderBy(o => o.Latency).Take(99).Average(o => o.Latency), 0).ToString();
            string averageRu = Math.Round(results.OrderBy(o => o.Latency).Take(99).Average(o => o.RU)).ToString();

            //Save summary back to benchmark
            benchmark.resultSummary = new ResultSummary(benchmark.testName, averageLatency, averageRu);

            Console.WriteLine($"\nSummary\n{Helpers.Line}\n");
            Console.WriteLine($"Test {results.Count} {benchmarkType} against {testName} account in {targetRegion}\n");
            Console.WriteLine($"Average Latency:\t{averageLatency} ms");
            Console.WriteLine($"Average Request Units:\t{averageRu} RUs\n\nPress any key to continue...\n");
            Console.ReadKey(true);
        }
    }

    class Result
    {
        public long Latency;
        public double RU;

        public Result(long latency, double ru)
        {
            Latency = latency;
            RU = ru;
        }
    }
    public class ResultSummary
    {
        public string testName;
        public string averageLatency;
        public string averageRu;

        public ResultSummary(string test, string latency, string Ru)
        {
            testName = test;
            averageLatency = latency;
            averageRu = Ru;
        }
    }
}
