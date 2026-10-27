using MafPlayground.AI.Workflows.Translation;
using MafPlayground.AI.Runtime.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

public sealed class ChatClientTranslationModelTests
{
    [Fact]
    public async Task DefaultRegistration_DefersClientUntilUncancelledModelExecution()
    {
        using FakeChatClient chatClient = new("""{"translatedText":"Hola"}""");
        chatClient.EnqueueResponse("""{"isValid":true,"confidence":0.95,"issues":[]}""");
        RecordingClientFactory factory = new(chatClient);
        ServiceCollection services = new();
        services.AddSingleton<IAIChatClientFactory>(factory);
        services.AddTranslationWorkflow();
        using ServiceProvider provider = services.BuildServiceProvider();
        ITranslationModel model = provider.GetRequiredService<ITranslationModel>();
        TranslationDraftRequest draft = new("Hello", "es", null, null);
        TranslationValidationRequest review = new("Hello", "es", "Hola", null, []);

        Assert.Empty(factory.EntityIds);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            model.TranslateAsync(draft, new CancellationToken(canceled: true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            model.ValidateAsync(review, new CancellationToken(canceled: true)));
        Assert.Empty(factory.EntityIds);

        string translated = await model.TranslateAsync(draft, CancellationToken.None);
        TranslationValidation validation = await model.ValidateAsync(review, CancellationToken.None);

        Assert.Equal("Hola", translated);
        Assert.True(validation.IsValid);
        Assert.Equal(["translation-workflow"], factory.EntityIds);
        Assert.Equal(2, chatClient.Requests.Count);
    }

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

    private sealed class RecordingClientFactory(IChatClient client) : IAIChatClientFactory
    {
        public List<string> EntityIds { get; } = [];

        public IChatClient Create(string entityId)
        {
            EntityIds.Add(entityId);
            return client;
        }
    }
}
