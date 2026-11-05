using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DynamicIsland.Modules.Translator.Services;

var architecture = RuntimeInformation.ProcessArchitecture.ToString();
Console.WriteLine($"Native smoke process architecture: {architecture}");
var expectedArchitecture = Environment.GetEnvironmentVariable("DI_EXPECTED_ARCH");
if (!string.IsNullOrEmpty(expectedArchitecture) && !string.Equals(architecture, expectedArchitecture, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Unexpected process architecture; emulation does not count as native acceptance.");
    return 1;
}

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: offline-smoke <isolated-model-directory> [--install]. --install downloads pinned models; without it no HTTP request is made.");
    return 2;
}
var install = args.Length == 2 && args[1] == "--install";
if (args.Length == 2 && !install) return 2;
using var http = new HttpClient(install ? new HttpClientHandler { AllowAutoRedirect = false } : new NoNetworkHandler())
    { Timeout = TimeSpan.FromMinutes(15) };
using var packages = new TranslationPackageManager(http, args[0]);
using var engine = new OfflineTranslationService(packages);
using var process = Process.GetCurrentProcess();
process.Refresh();
var startupMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
var baselineWorkingSet = process.WorkingSet64;
var observations = new List<object>();
foreach (var package in packages.AvailablePackages)
{
    if (install)
        await packages.InstallAsync(package.SourceLanguage, package.TargetLanguage, null, CancellationToken.None);
    var before = Process.GetCurrentProcess().TotalProcessorTime;
    var timer = Stopwatch.StartNew();
    var input = package.SourceLanguage == "en" ? "Good morning. The number is 42." : "Доброе утро. Число — 42.";
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var result = await engine.TranslateAsync(input, package.SourceLanguage, package.TargetLanguage, timeout.Token);
    timer.Stop();
    var cpu = Process.GetCurrentProcess().TotalProcessorTime - before;
    // These fixed synthetic smoke inputs are intentionally printable; user translations are never logged.
    Console.WriteLine($"{package.SourceLanguage}→{package.TargetLanguage}: {input} → {result.Text}; {timer.Elapsed.TotalSeconds:F2}s wall, {cpu.TotalSeconds:F2}s CPU, {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MiB peak working set");
    process.Refresh();
    observations.Add(new { package.SourceLanguage, package.TargetLanguage, package.Revision, package.DownloadBytes,
        wallMs = timer.Elapsed.TotalMilliseconds, cpuMs = cpu.TotalMilliseconds,
        peakWorkingSetBytes = process.PeakWorkingSet64, workingSetBytes = process.WorkingSet64 });
    // This is a synthetic inference/target-control smoke, not a quality score.
    if (!result.Text.Contains("42", StringComparison.Ordinal)) return 1;
    if (package.TargetLanguage == "ru" && !result.Text.Contains("утро", StringComparison.OrdinalIgnoreCase)) return 1;
    if (package.TargetLanguage == "en" && !result.Text.Contains("morning", StringComparison.OrdinalIgnoreCase)) return 1;
    if (TranslationSentenceSplitter.Split(result.Text).Length < 2) return 1;
}
// Native engine host only; this is not a measurement of the full WPF app.
// No forced collection, memory trim or model caching changes are requested.
process.Refresh();
var idleCpuStart = process.TotalProcessorTime;
var idleTimer = Stopwatch.StartNew();
await Task.Delay(TimeSpan.FromSeconds(20));
process.Refresh();
var metrics = new
{
    description = "Native offline engine smoke host; not the full WPF application",
    architecture, runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription, install, startupMs, baselineWorkingSet,
    requests = observations, idleWindowMs = idleTimer.Elapsed.TotalMilliseconds,
    idleCpuOneCorePercent = 100 * (process.TotalProcessorTime - idleCpuStart).TotalMilliseconds / idleTimer.Elapsed.TotalMilliseconds,
    idleWorkingSetBytes = process.WorkingSet64, idlePrivateBytes = process.PrivateMemorySize64,
    processPeakWorkingSetBytes = process.PeakWorkingSet64,
};
var serialized = JsonSerializer.Serialize(metrics);
Console.WriteLine(serialized);
if (Environment.GetEnvironmentVariable("DI_OFFLINE_METRICS") is { Length: > 0 } metricsFile)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(metricsFile))!);
    await File.WriteAllTextAsync(metricsFile, serialized);
}
return 0;

internal sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Network is forbidden during the offline smoke check.");
}
