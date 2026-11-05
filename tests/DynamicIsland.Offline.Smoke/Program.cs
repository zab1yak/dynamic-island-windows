using System.Diagnostics;
using System.Runtime.InteropServices;
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
    // This is a synthetic inference/target-control smoke, not a quality score.
    if (!result.Text.Contains("42", StringComparison.Ordinal)) return 1;
    if (package.TargetLanguage == "ru" && !result.Text.Contains("утро", StringComparison.OrdinalIgnoreCase)) return 1;
    if (package.TargetLanguage == "en" && !result.Text.Contains("morning", StringComparison.OrdinalIgnoreCase)) return 1;
    if (TranslationSentenceSplitter.Split(result.Text).Length < 2) return 1;
}
return 0;

internal sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Network is forbidden during the offline smoke check.");
}
