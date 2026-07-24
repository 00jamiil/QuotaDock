namespace QuotaDock.Connectors.Dashboard;

public sealed class DashboardNavigationPolicy
{
    private readonly HashSet<string> allowedDomains;

    public DashboardNavigationPolicy(IEnumerable<string> allowedDomains)
    {
        ArgumentNullException.ThrowIfNull(allowedDomains);
        this.allowedDomains = new HashSet<string>(
            allowedDomains.Select(domain => domain.Trim().Trim('.').ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        if (this.allowedDomains.Count == 0 || this.allowedDomains.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one valid domain is required.", nameof(allowedDomains));
        }
    }

    public bool IsAllowed(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri || !string.Equals(destination.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = destination.IdnHost.TrimEnd('.');
        return allowedDomains.Any(domain =>
            string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));
    }
}

