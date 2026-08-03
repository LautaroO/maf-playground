using MafPlayground.AI.Workflows.Translation;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

public sealed class ChatClientTranslationModelTests
{
    [Fact]
    public async Task TranslateAsync_RequestsAndReadsStructuredOutput()
    {
        using FakeChatClient chatClient = new("""
            {"translatedText":"Hola"}
            """);
        ChatClientTranslationModel model = new(chatClient);

        string result = await model.TranslateAsync(
            "Hello",
            "es",
            repairIssues: null,
            CancellationToken.None);

        Assert.Equal("Hola", result);
        ChatOptions options = Assert.Single(chatClient.RequestOptions)!;
        Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
    }

    [Fact]
    public async Task ValidateAsync_NormalizesStructuredReview()
    {
        using FakeChatClient chatClient = new("""
            {"isValid":false,"confidence":1.4,"issues":[" Wrong language ",""]}
            """);
        ChatClientTranslationModel model = new(chatClient);

        TranslationValidation result = await model.ValidateAsync(
            "Hello",
            "es",
            "Hello",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(1, result.Confidence);
        Assert.Equal(["Wrong language"], result.Issues);
    }
}
