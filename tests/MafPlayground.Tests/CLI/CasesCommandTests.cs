using System.Text.Json;
using MafPlayground.CLI.Commands;
using MafPlayground.CLI.Tui;

namespace MafPlayground.Tests;

public sealed class CasesCommandTests
{
    [Theory]
    [InlineData("hello", 0)]
    [InlineData("absent", 1)]
    public async Task RunsSavedChecksAndWritesOnlyFailedCaptures(string expected, int exit)
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            const string id = "basic-agent";
            var store = new CaseStore(id, root.FullName);
            var item = new StudioCase(Guid.NewGuid(), "Greeting", JsonSerializer.SerializeToElement(new { message = "Hi" }), new(Contains: expected));
            store.Save(item, true);
            // The passing check lives beyond the display capture limit.
            string response = exit == 0 ? new string('x', 70_000) + "hello" : "hello";
            var output = new StringWriter();
            string artifacts = Path.Combine(root.FullName, "artifacts");
            int code = await CasesCommand.RunAsync(id, "ollama:fixture", root.FullName, artifacts, output,
                (entity, input, model, publish, token) =>
                {
                    Assert.Equal("ollama:fixture", model);
                    Assert.Equal("Hi", JsonSerializer.Deserialize<JsonElement>(input).GetProperty("message").GetString());
                    publish(run => { run.Begin(entity.Id, item.Input); run.End(entity.Id, response); run.Complete(response); });
                    return Task.CompletedTask;
                }, CancellationToken.None);
            Assert.Equal(exit, code);
            Assert.Contains(exit == 0 ? "PASS  Greeting" : "FAIL  Greeting", output.ToString());
            Assert.Equal(exit != 0, Directory.Exists(artifacts));
            if (exit != 0)
            {
                var capture = LocalCapture.Read(Assert.Single(Directory.GetFiles(artifacts)));
                Assert.Equal(id, capture.Entity);
                Assert.Equal("hello", JsonSerializer.Deserialize<string>(capture.Result));
                Assert.True(Assert.Single(capture.Invocations[id]).Finished);
            }
        }
        finally { root.Delete(true); }
    }
}
