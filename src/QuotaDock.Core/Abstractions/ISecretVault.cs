namespace QuotaDock.Core.Abstractions;

public interface ISecretVault
{
    ValueTask SaveAsync(string reference, string secret, CancellationToken cancellationToken = default);
    ValueTask<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default);
    ValueTask RemoveAsync(string reference, CancellationToken cancellationToken = default);
}

