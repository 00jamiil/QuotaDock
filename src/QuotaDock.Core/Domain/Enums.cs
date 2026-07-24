namespace QuotaDock.Core.Domain;

public enum ProviderKind
{
    OpenAI,
    Anthropic,
    Alibaba
}

public enum MetricKind
{
    QuotaPercentage,
    Credits,
    Tokens,
    Requests,
    Currency
}

public enum MetricDirection
{
    Used,
    Remaining
}

public enum MetricScope
{
    Session,
    Weekly,
    Monthly,
    Model,
    Project,
    Account
}

public enum DataSourceKind
{
    OfficialApi,
    LocalCli,
    DashboardReader
}

public enum ConnectionHealth
{
    Fresh,
    Stale,
    AuthenticationRequired,
    RateLimited,
    Unavailable,
    FormatChanged
}

[Flags]
public enum ConnectorCapabilities
{
    None = 0,
    Quota = 1 << 0,
    Credits = 1 << 1,
    Tokens = 1 << 2,
    Requests = 1 << 3,
    Costs = 1 << 4,
    ResetTimes = 1 << 5,
    ProjectBreakdown = 1 << 6,
    ModelBreakdown = 1 << 7
}

