using System.Text.Json;
using MafPlayground.CLI.Tui;

namespace MafPlayground.Tests;

public sealed class StudioCaseTests
{
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);

    [Fact]
    public void ChecksMatchTranslationFieldsAndSelectFirstExcessAttempt()
    {
        var checks = new CaseChecks("247", ["es", "fr"], "/Translations/0/TranslatedText", "draft", 2);
        var result = Json("""{"Translations":[{"TargetLanguage":"es","TranslatedText":"Pedido 247"},{"TargetLanguage":"fr"}]}""");
        Assert.Empty(checks.Evaluate("Completed", result, ["draft", "draft"], true));
        var failure = Assert.Single(checks.Evaluate("Completed", result, ["draft", "review", "draft", "draft"], true));
        Assert.Equal(("draft", 2), (failure.Node, failure.Index));
        Assert.Equal(4, checks.Evaluate("Completed", Json("""{"text":"es fr"}"""), [], false).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemoteCaseTargetsTheFirstExcessInvocationAndRejectsUnverifiableLimits(bool complete)
    {
        var journal = new RemoteJournal("r");
        var snapshot = JsonSerializer.SerializeToElement(new { runId = "r", status = 2, isTerminal = true, latestSequence = 3, captureComplete = complete });
        var page = JsonSerializer.SerializeToElement(new
        {
            events = Enumerable.Range(1, 3).Select(sequence =>
            new { runId = "r", sequence, kind = "executor_started", executorId = "draft", invocationId = "inv-" + sequence })
        });
        journal.Apply(snapshot, page);
        var limit = new CaseChecks(Node: "draft", MaxInvocations: 2);
        var (failures, target) = RemoteWorkspace.EvaluateCase(journal, limit);
        Assert.Single(failures);
        Assert.Equal("inv-3", target.Str("invocationId"));
        Assert.Equal(complete, RemoteWorkspace.EvaluateCase(journal, limit with { MaxInvocations = 3 }).Failures.Count == 0);
        Assert.Single(RemoteWorkspace.EvaluateCase(journal, limit with { Node = "unknown" }).Failures);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/translations/0/translatedText")]
    public void TextInSourceDoesNotSatisfyATranslationCheck(string? pointer)
    {
        var checks = new CaseChecks(Contains: "247", Pointer: pointer);
        var result = Json("""{"sourceText":"Order 247","translations":[{"targetLanguage":"es","translatedText":"Pedido"}]}""");
        Assert.Single(checks.Evaluate("Completed", result, [], true));
    }

    [Fact]
    public async Task SuiteEvaluatesInOrderAndStopsAfterCancellation()
    {
        using var stop = new CancellationTokenSource();
        var items = new[] { "first", "second", "third" }.Select(name =>
            new StudioCase(Guid.NewGuid(), name, Json("1"), new())).ToArray();
        List<string> events = [];
        await CaseLibrary.ExecuteAsync(items, async (item, token) =>
        {
            await Task.Yield();
            if (item.Name == "second")
            {
                stop.Cancel();
                token.ThrowIfCancellationRequested();
            }
            return new(item.Name, [new("check failed")]);
        }, name => events.Add("start " + name), outcome => events.Add(outcome.Failures.Single().Message), stop.Token);
        Assert.Equal(["start first", "check failed", "start second", "Cancelled"], events);
    }

    [Fact]
    public void CasesAreExplicitEntityScopedAndEditsSurviveReopening()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var store = new CaseStore("agent", directory.FullName);
            var item = new StudioCase(Guid.NewGuid(), "Example", Json("""{"prompt":"one"}"""), new());
            store.Save(item, create: true);
            Assert.Empty(new CaseStore("agent/other", directory.FullName).Read());
            Assert.Throws<IOException>(() => store.Save(item, create: true));
            store.Save(item with { Name = "Renamed" }, create: false);
            var reopened = new CaseStore("agent", directory.FullName);
            Assert.Equal("Renamed", Assert.Single(reopened.Read()).Name);
            reopened.Delete(item.Id);
            Assert.Empty(reopened.Read());
        }
        finally { directory.Delete(recursive: true); }
    }
}
