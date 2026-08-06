using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;

namespace MafPlayground.Tests.AI.Guards;

public sealed class ContentGuardTests
{
    private readonly ContentGuard _guard = new(new RegexPiiContentInspector());

    [Fact]
    public async Task Redact_ReplacesSensitiveValuesWithoutReturningThem()
    {
        const string input = "Contact jane@example.com or +54 11 5555-1234.";

        string result = await _guard.ApplyAsync(
            input,
            GuardAction.Redact,
            ContentOrigin.UserInput);

        Assert.DoesNotContain("jane@example.com", result, StringComparison.Ordinal);
        Assert.DoesNotContain("5555-1234", result, StringComparison.Ordinal);
        Assert.Contains("<EMAIL_1>", result, StringComparison.Ordinal);
        Assert.Contains("<PHONE_1>", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Block_ReportsCategoriesButNotSensitiveValues()
    {
        const string secret = "jane@example.com";

        ContentGuardRejectedException exception = await Assert.ThrowsAsync<
            ContentGuardRejectedException>(async () => await _guard.ApplyAsync(
                secret,
                GuardAction.Block,
                ContentOrigin.ToolArgument));

        Assert.Contains("EMAIL", exception.Categories);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allow_DoesNotInspectOrModifyContent()
    {
        const string input = "jane@example.com";

        string result = await _guard.ApplyAsync(
            input,
            GuardAction.Allow,
            ContentOrigin.UserInput);

        Assert.Equal(input, result);
    }
}

