using CosmosGlobalDistDemosCore;
using Xunit;

public class FailoverTests
{
    [Theory]
    [InlineData("East US 2", false, "East US 2")]
    [InlineData("East US 2", true, null)]
    [InlineData("westus2", false, null)]
    [InlineData("westus2", true, "westus2")]
    public void SuccessfulReadIdentifiesFailoverOrRecovery(string contacted, bool recovery, string expected)
    {
        Assert.Equal(expected, MultiMasterFailover.MatchReadRegion(new[] { contacted }, "West US 2", recovery));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrAmbiguousDiagnosticsDoNotConfirmARegionChange(bool recovery)
    {
        Assert.Null(MultiMasterFailover.MatchReadRegion(new string[0], "West US 2", recovery));
        Assert.Null(MultiMasterFailover.MatchReadRegion(new[] { "West US 2", "East US 2" }, "West US 2", recovery));
    }
}
