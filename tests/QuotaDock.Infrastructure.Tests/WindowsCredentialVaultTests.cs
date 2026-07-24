using QuotaDock.Infrastructure.Security;

namespace QuotaDock.Infrastructure.Tests;

public sealed class WindowsCredentialVaultTests
{
    [Fact]
    public async Task SaveRetrieveRemove_RoundTripsWithinTheNamespacedVault()
    {
        var vault = new WindowsCredentialVault("QuotaDock.Tests");
        var reference = $"roundtrip-{Guid.NewGuid():N}";
        var secret = $"test-value-{Guid.NewGuid():N}";

        try
        {
            await vault.SaveAsync(reference, secret);

            Assert.Equal(secret, await vault.RetrieveAsync(reference));
        }
        finally
        {
            await vault.RemoveAsync(reference);
        }

        Assert.Null(await vault.RetrieveAsync(reference));
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains/slash")]
    [InlineData("contains\\slash")]
    public async Task SaveAsync_RejectsUnsafeReferences(string reference)
    {
        var vault = new WindowsCredentialVault("QuotaDock.Tests");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await vault.SaveAsync(reference, "not-a-real-secret"));
    }
}
