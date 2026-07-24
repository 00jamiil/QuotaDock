using QuotaDock.Infrastructure.Diagnostics;

namespace QuotaDock.Infrastructure.Tests;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("Authorization: Bearer abc.def.ghi", "Authorization: Bearer [REDACTED]")]
    [InlineData("x-api-key: sk-ant-admin01-super-secret", "x-api-key: [REDACTED]")]
    [InlineData("key=sk-admin-openai-secret", "key=[REDACTED]")]
    [InlineData("Cookie: sessionKey=secret-cookie", "Cookie: [REDACTED]")]
    public void Redact_RemovesKnownSecretShapes(string input, string expected)
    {
        Assert.Equal(expected, SecretRedactor.Redact(input));
    }

    [Fact]
    public void Redact_LeavesOrdinaryStatusTextUntouched()
    {
        const string message = "OpenAI asked QuotaDock to slow down for 90 seconds.";

        Assert.Equal(message, SecretRedactor.Redact(message));
    }
}

