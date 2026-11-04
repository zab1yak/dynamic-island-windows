using System.Diagnostics;
using DynamicIsland.Modules.Translator.Services;

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
    var input = package.SourceLanguage == "en" ? "Hello" : "Привет";
    var result = await engine.TranslateAsync(input, package.SourceLanguage, package.TargetLanguage, CancellationToken.None);
    timer.Stop();
    var cpu = Process.GetCurrentProcess().TotalProcessorTime - before;
    // These fixed synthetic smoke inputs are intentionally printable; user translations are never logged.
    Console.WriteLine($"{package.SourceLanguage}→{package.TargetLanguage}: {input} → {result.Text}; {timer.Elapsed.TotalSeconds:F2}s wall, {cpu.TotalSeconds:F2}s CPU, {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576} MiB peak working set");
    if (package.TargetLanguage == "ru" && !result.Text.Contains("привет", StringComparison.OrdinalIgnoreCase)) return 1;
    if (package.TargetLanguage == "en" && !result.Text.Contains("hello", StringComparison.OrdinalIgnoreCase) && !result.Text.Contains("hi", StringComparison.OrdinalIgnoreCase) && !result.Text.Contains("hey", StringComparison.OrdinalIgnoreCase)) return 1;
}
return 0;

internal sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Network is forbidden during the offline smoke check.");
}
