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
            new TranslationDraftRequest("Hello", "es", null, null),
            CancellationToken.None);

        Assert.Equal("Hola", result);
        ChatOptions options = Assert.Single(chatClient.RequestOptions)!;
        Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
    }

    [Fact]
    public async Task TranslateAsync_SendsPreviousDraftForScopedRepair()
    {
        using FakeChatClient chatClient = new("""
            {"translatedText":"Pedido 247 confirmado."}
            """);
        ChatClientTranslationModel model = new(chatClient);

        await model.TranslateAsync(
            new TranslationDraftRequest(
                "Order 247 confirmed.",
                "es",
                "Pedido confirmado.",
                ["MissingData: Preserve order number 247."]),
            CancellationToken.None);

        string requestText = Assert.Single(chatClient.Requests).Single().Text;
        Assert.Contains(
            "\"previousTranslatedText\":\"Pedido confirmado.\"",
            requestText,
            StringComparison.Ordinal);
        Assert.Contains("MissingData", requestText, StringComparison.Ordinal);
        Assert.DoesNotContain("Order 247 confirmed.", requestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_NormalizesStructuredReview()
    {
        using FakeChatClient chatClient = new("""
            {"isValid":false,"confidence":1.4,"issues":[{"severity":"Blocking","code":"WrongTargetLanguage","description":" Wrong language "}]}
            """);
        ChatClientTranslationModel model = new(chatClient);

        TranslationValidation result = await model.ValidateAsync(
            new TranslationValidationRequest(
                "Hello",
                "es",
                "Hello",
                null,
                []),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(1, result.Confidence);
        TranslationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationIssueSeverity.Blocking, issue.Severity);
        Assert.Equal(TranslationIssueCode.WrongTargetLanguage, issue.Code);
        Assert.Equal("Wrong language", issue.Description);
    }

    [Fact]
    public async Task ValidateAsync_SendsPreviousIssuesAndReadsTheirResolution()
    {
        using FakeChatClient chatClient = new("""
            {"isValid":true,"confidence":0.95,"issues":[],"previousIssueResolutions":[{"issueId":"issue-1","status":"Resolved"}]}
            """);
        ChatClientTranslationModel model = new(chatClient);

        TranslationValidation result = await model.ValidateAsync(
            new TranslationValidationRequest(
                "Hello",
                "es",
                "Hola",
                "Hello",
                [new TranslationIssueReference(
                    "issue-1",
                    TranslationIssueCode.WrongTargetLanguage,
                    "The text is not Spanish.")]),
            CancellationToken.None);

        TranslationIssueResolution resolution = Assert.Single(
            result.PreviousIssueResolutions!);
        Assert.Equal("issue-1", resolution.IssueId);
        Assert.Equal(TranslationIssueResolutionStatus.Resolved, resolution.Status);
        string requestText = Assert.Single(chatClient.Requests).Single().Text;
        Assert.Contains("\"previousBlockingIssues\"", requestText, StringComparison.Ordinal);
        Assert.Contains("\"previousTranslatedText\":\"Hello\"", requestText, StringComparison.Ordinal);
        Assert.Contains("\"issue-1\"", requestText, StringComparison.Ordinal);
    }
}
