using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CosmosGlobalDistDemosCore;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Moq;
using Xunit;

public class BenchmarkTests
{
    private static readonly string[] Sample = Enumerable.Range(1, 100).Select(i => i.ToString()).ToArray();
    private readonly Mock<Container> container = new(MockBehavior.Strict);
    private readonly Benchmark benchmark;

    public BenchmarkTests()
    {
        benchmark = new Benchmark { container = container.Object, partitionKeyValue = "demo-partition" };
    }

    [Fact]
    public async Task ExistingDataIsReusedWithoutExecutingAProcedure()
    {
        Queries().Returns(Page(Sample));
        Assert.Equal(Sample, await Benchmark.GetOrSeedIds(benchmark));
    }

    [Fact]
    public async Task PartialExistingSampleWaitsWithoutReseeding()
    {
        Queries().Returns(Page(Sample[..3])).Returns(Page(Sample[..99].Append(Sample[0]).ToArray())).Returns(Page(Sample));
        Assert.Equal(Sample, await Benchmark.GetOrSeedIds(benchmark));
    }

    [Fact]
    public async Task IncompleteSampleTimesOutRatherThanStartingAShortBenchmark()
    {
        container.Setup(c => c.GetItemQueryIterator<string>(It.IsAny<QueryDefinition>(), null, It.IsAny<QueryRequestOptions>()))
            .Returns(() => Page(Sample[..99]));
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            Benchmark.GetOrSeedIds(benchmark, requiredCount: 100, visibilityTimeout: TimeSpan.FromMilliseconds(20)));
        Assert.Contains("99 of 100", error.Message);
    }

    [Fact]
    public async Task FailoverOnlyNeedsOneVisibleItem()
    {
        Queries().Returns(Page("existing"));
        Assert.Equal("existing", Assert.Single(await Benchmark.GetOrSeedIds(benchmark, requiredCount: 1)));
    }

    [Fact]
    public async Task EmptyPartitionIsSeededInBatchesAndReadFromTheTargetRegion()
    {
        Queries().Returns(Page()).Returns(Page()).Returns(Page(Sample[..3])).Returns(Page(Sample));
        var batches = new List<int>();
        BulkImport().Returns((string _, PartitionKey _, object[] args, StoredProcedureRequestOptions _, CancellationToken _) =>
        {
            var customers = Assert.IsType<SampleCustomer[]>(Assert.Single(args));
            Assert.All(customers, c => Assert.Equal(benchmark.partitionKeyValue, c.MyPartitionKey));
            batches.Add(customers.Length);
            return Task.FromResult(Mock.Of<StoredProcedureExecuteResponse<int>>(r => r.Resource == 50));
        });

        Assert.Equal(Sample, await Benchmark.GetOrSeedIds(benchmark));
        Assert.Equal(new[] { 100, 50 }, batches);
    }

    [Fact]
    public async Task ImportWithoutProgressStopsBeforeTryingToReadASample()
    {
        Queries().Returns(Page());
        BulkImport().ReturnsAsync(Mock.Of<StoredProcedureExecuteResponse<int>>(r => r.Resource == 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Benchmark.GetOrSeedIds(benchmark));
    }

    private Moq.Language.ISetupSequentialResult<FeedIterator<string>> Queries() =>
        container.SetupSequence(c => c.GetItemQueryIterator<string>(It.IsAny<QueryDefinition>(), null,
            It.Is<QueryRequestOptions>(o => o.PartitionKey == new PartitionKey(benchmark.partitionKeyValue))));

    private Moq.Language.Flow.ISetup<Scripts, Task<StoredProcedureExecuteResponse<int>>> BulkImport()
    {
        var scripts = new Mock<Scripts>(MockBehavior.Strict);
        container.SetupGet(c => c.Scripts).Returns(scripts.Object);
        return scripts.Setup(s => s.ExecuteStoredProcedureAsync<int>("BulkImport", new PartitionKey(benchmark.partitionKeyValue),
            It.IsAny<object[]>(), null, It.IsAny<CancellationToken>()));
    }

    private static FeedIterator<string> Page(params string[] ids)
    {
        var feed = new Mock<FeedResponse<string>>();
        feed.Setup(f => f.GetEnumerator()).Returns(() => ((IEnumerable<string>)ids).GetEnumerator());
        var iterator = new Mock<FeedIterator<string>>();
        iterator.SetupSequence(i => i.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(feed.Object);
        return iterator.Object;
    }
}
