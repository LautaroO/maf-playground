using System.Runtime.CompilerServices;
using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Context;
using MafPlayground.Studio.Http;
using Microsoft.Extensions.AI;

// TEST ONLY. The production CLI has no deterministic provider or fixture flags.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Configuration["AI:Defaults:Model"] = "fixture:deterministic";
var access = new StudioAccessPolicy("http://127.0.0.1:5053");
builder.WebHost.UseUrls(access.Endpoint.AbsoluteUri);
builder.Services.AddSingleton(access);
builder.Services.AddSingleton<IChatClientProvider, FixtureProvider>();
builder.Services.AddSingleton<IUserContextAccessor, FixtureUser>();
builder.Services.AddAIRuntime(builder.Configuration).AddMafPlaygroundSampleEntities();
builder.Services.AddInMemoryAgentSessions();
builder.Services.AddStudio(builder.Configuration);
await using var app = builder.Build();
app.UseStudioAccess();
app.MapStudio();
await app.StartAsync();
// Parent test process captures this pipe in memory; never emit it to test logs/artifacts.
Console.WriteLine("FIXTURE_READY " + access.BootstrapCode);
await app.WaitForShutdownAsync();

internal sealed class FixtureUser : IUserContextAccessor { public UserContext GetCurrent() => new([]); }
internal sealed class FixtureProvider : IChatClientProvider
{
  public string Name => "fixture";
  public IChatClient CreateChatClient(string model) => new FixtureClient();
}
internal sealed class FixtureClient : IChatClient
{
  public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
  {
    ChatMessage[] input = messages.ToArray();
    string last = input.Last(message => message.Role == ChatRole.User).Text;
    if (last == "fixture-wait") await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    if (last.StartsWith('{') && JsonDocument.Parse(last).RootElement.TryGetProperty("targetLanguage", out _)) return Translation(last);
    FunctionResultContent? tool = input.SelectMany(message => message.Contents).OfType<FunctionResultContent>().LastOrDefault();
    if (last == "fixture-time" && tool is null)
      return new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("fixture-time-call", "get_current_date_time", new Dictionary<string, object?> { ["timeZoneId"] = "UTC" })]));
    string reply = last == "fixture-time" ? "Tool result: " + JsonSerializer.Serialize(tool!.Result) :
        "User turns: " + string.Join(" | ", input.Where(message => message.Role == ChatRole.User).Select(message => message.Text));
    return new(new ChatMessage(ChatRole.Assistant, reply));
  }
  private static ChatResponse Translation(string input)
  {
    using JsonDocument json = JsonDocument.Parse(input);
    JsonElement root = json.RootElement;
    string language = root.GetProperty("targetLanguage").GetString()!;
    object reply;
    if (!root.TryGetProperty("translatedText", out _))
      reply = new { translatedText = language == "fr" ? "Votre commande est prête." : "Su pedido 247 está listo." };
    else if (root.TryGetProperty("previousBlockingIssues", out var issues) && issues.GetArrayLength() > 0)
      reply = new { isValid = false, confidence = 0.98, previousIssueResolutions = issues.EnumerateArray().Select(issue => new { issueId = issue.GetProperty("id").GetString(), status = "StillPresent" }).ToArray() };
    else reply = new { isValid = true, confidence = 0.98, issues = Array.Empty<object>() };
    return new(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(reply)));
  }
  public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
    yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = response.Messages.SelectMany(message => message.Contents).ToList(), FinishReason = response.FinishReason };
  }
  public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
  public void Dispose() { }
}
