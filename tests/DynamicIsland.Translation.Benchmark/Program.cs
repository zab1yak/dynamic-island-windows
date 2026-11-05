using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DynamicIsland.Modules.Translator.Services;

if (args.Length is < 3 or > 5)
{
    Console.Error.WriteLine("Usage: translation-benchmark <installed-model-directory> <corpus.json> <results.jsonl>. Never downloads models.");
    return 2;
}
var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var cases = JsonSerializer.Deserialize<BenchmarkCase[]>(await File.ReadAllTextAsync(args[1]), json)!;
using var http = new HttpClient(new NoNetworkHandler());
using var packages = new TranslationPackageManager(http, args[0]);
using var engine = new OfflineTranslationService(packages, new(args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1, args.Length > 4 && args[4] == "--sentences"));
await using var output = new StreamWriter(args[2], append: true) { AutoFlush = true };
foreach (var item in cases)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    using var process = Process.GetCurrentProcess();
    var cpu = process.TotalProcessorTime;
    var timer = Stopwatch.StartNew();
    string? translated = null, error = null;
    try { translated = (await engine.TranslateAsync(item.Text, item.Source, item.Target, timeout.Token)).Text; }
    catch (Exception exception) when (exception is TranslationException or OperationCanceledException)
    { error = exception.GetType().Name + ": " + exception.Message; }
    timer.Stop();
    process.Refresh();
    await output.WriteLineAsync(JsonSerializer.Serialize(new
    {
        item.Id, item.Category, item.Source, item.Target, item.Text, item.References,
        output = translated, error, wallMs = timer.Elapsed.TotalMilliseconds,
        cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
        peakWorkingSetBytes = process.PeakWorkingSet64, workingSetBytes = process.WorkingSet64,
        runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        os = RuntimeInformation.OSDescription
    }));
    Console.WriteLine($"{item.Id}: {(error is null ? "OK" : "ERROR")} {timer.Elapsed.TotalSeconds:F2}s");
}
return 0;

internal sealed record BenchmarkCase(string Id, string Category, string Source, string Target, string Text, string[] References);
internal sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Network is forbidden during the quality benchmark.");
}
