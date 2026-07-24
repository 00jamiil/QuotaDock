using QuotaDock.Connectors.Dashboard;

namespace QuotaDock.Connectors.Tests;

public sealed class DashboardNavigationPolicyTests
{
    private static readonly DashboardNavigationPolicy Policy = new([
        "alibabacloud.com",
        "aliyun.com"
    ]);

    [Theory]
    [InlineData("https://modelstudio.console.alibabacloud.com/token-plan")]
    [InlineData("https://account.alibabacloud.com/login")]
    [InlineData("https://signin.aliyun.com/")]
    public void IsAllowed_AcceptsHttpsProviderDomains(string address)
    {
        Assert.True(Policy.IsAllowed(new Uri(address)));
    }

    [Theory]
    [InlineData("http://modelstudio.console.alibabacloud.com/token-plan")]
    [InlineData("https://alibabacloud.com.evil.example/")]
    [InlineData("file:///C:/secrets.txt")]
    [InlineData("javascript:alert(1)")]
    public void IsAllowed_RejectsUnsafeOrLookalikeDestinations(string address)
    {
        Assert.False(Policy.IsAllowed(new Uri(address)));
    }

    [Fact]
    public void Constructor_NormalizesDomainsAndRejectsEmptyConfiguration()
    {
        var normalized = new DashboardNavigationPolicy(["  .Claude.AI.  "]);

        Assert.True(normalized.IsAllowed(new Uri("https://claude.ai/settings/usage")));
        Assert.Throws<ArgumentException>(() => new DashboardNavigationPolicy([]));
    }
}
