using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using DynamicIsland.Modules.Translator.Services;

namespace DynamicIsland.Tests.Translator;

public sealed class TranslationPackageTests
{
    private static readonly byte[] ModelBytes = Encoding.UTF8.GetBytes("verified model test fixture");
    private static TranslationModel TestModel => new("en", "ru", "onnx-community/opus-mt-en-ru", "immutable-test-revision",
        [new("onnx/encoder_model_quantized.onnx", ModelBytes.Length, Convert.ToHexString(SHA256.HashData(ModelBytes)))]);

    [Fact]
    public async Task VerifiedInstall_IsPublishedAtomically_AndCanBeRemoved()
    {
        using var folder = new TempFolder();
        var requests = 0;
        using var manager = Manager(folder.Path, request =>
        {
            requests++;
            Assert.Equal("https://huggingface.co/onnx-community/opus-mt-en-ru/resolve/immutable-test-revision/onnx/encoder_model_quantized.onnx", request.RequestUri!.AbsoluteUri);
            Assert.False(File.Exists(Path.Combine(folder.Path, "en-ru", "installed")));
            return Response(ModelBytes);
        });
        Assert.False(manager.IsInstalled("en", "ru"));
        await manager.InstallAsync("en", "ru", null, TestContext.Current.CancellationToken);
        Assert.True(manager.IsInstalled("en", "ru"));
        Assert.Contains("ONNX Community", await File.ReadAllTextAsync(Path.Combine(manager.GetInstalledPath("en", "ru"), "ATTRIBUTION.txt"), TestContext.Current.CancellationToken));
        Assert.Equal(ModelBytes, await File.ReadAllBytesAsync(Path.Combine(manager.GetInstalledPath("en", "ru"), "encoder_model_quantized.onnx"), TestContext.Current.CancellationToken));
        await manager.InstallAsync("en", "ru", null, TestContext.Current.CancellationToken);
        Assert.Equal(1, requests);
        await manager.RemoveAsync("en", "ru", TestContext.Current.CancellationToken);
        Assert.False(manager.IsInstalled("en", "ru"));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("short")]
    [InlineData("oversize")]
    [InlineData("redirect")]
    public async Task UntrustedOrIncompleteBytes_NeverBecomeInstalled(string fault)
    {
        using var folder = new TempFolder();
        using var manager = Manager(folder.Path, request =>
        {
            var bytes = fault switch
            {
                "hash" => Enumerable.Repeat((byte)7, ModelBytes.Length).ToArray(),
                "short" => ModelBytes[..^1],
                "oversize" => ModelBytes.Concat(new byte[] { 0 }).ToArray(),
                _ => ModelBytes,
            };
            var response = Response(bytes);
            if (fault == "redirect") response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://untrusted.example/model");
            return response;
        });
        await Assert.ThrowsAsync<IOException>(() => manager.InstallAsync("en", "ru", null, TestContext.Current.CancellationToken));
        Assert.False(manager.IsInstalled("en", "ru"));
        Assert.Empty(Directory.GetDirectories(folder.Path));
    }

    [Fact]
    public async Task CancellationCleansStaging_AndNextAttemptWorks()
    {
        using var folder = new TempFolder();
        using var cancel = new CancellationTokenSource();
        var attempt = 0;
        using var manager = Manager(folder.Path, _ =>
        {
            if (++attempt == 1) cancel.Cancel();
            return Response(ModelBytes);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.InstallAsync("en", "ru", null, cancel.Token));
        Assert.False(manager.IsInstalled("en", "ru"));
        Assert.Empty(Directory.GetDirectories(folder.Path));
        await manager.InstallAsync("en", "ru", null, TestContext.Current.CancellationToken);
        Assert.True(manager.IsInstalled("en", "ru"));
    }

    [Fact]
    public void InterruptedInstallWithoutMarker_IsNotExposedAsReady()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "en-ru"));
        File.WriteAllBytes(Path.Combine(folder.Path, "en-ru", "encoder_model_quantized.onnx"), ModelBytes);
        using var manager = Manager(folder.Path, _ => throw new InvalidOperationException("No download requested."));
        Assert.False(manager.IsInstalled("en", "ru"));
        var error = Assert.Throws<TranslationException>(() => manager.GetInstalledPath("en", "ru"));
        Assert.Equal(TranslationFailure.NotConfigured, error.Failure);
    }

    [Theory]
    [InlineData("http://huggingface.co/model")]
    [InlineData("https://untrusted.example/model")]
    public async Task InsecureRedirectIsRejectedBeforeFollowingIt(string target)
    {
        using var folder = new TempFolder();
        var calls = 0;
        using var manager = Manager(folder.Path, _ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(target);
            return response;
        });
        await Assert.ThrowsAsync<IOException>(() => manager.InstallAsync("en", "ru", null, TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
        Assert.False(manager.IsInstalled("en", "ru"));
    }

    [Fact]
    public async Task OtherProcessOperationLockPreventsStaleCleanupOrDownload()
    {
        using var folder = new TempFolder();
        using var operationLock = new FileStream(Path.Combine(folder.Path, ".package-operation.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var manager = Manager(folder.Path, _ => throw new InvalidOperationException("No request while lock held."));
        await Assert.ThrowsAsync<IOException>(() => manager.InstallAsync("en", "ru", null, TestContext.Current.CancellationToken));
        Assert.False(manager.IsInstalled("en", "ru"));
    }

    [Fact]
    public async Task RestartedInstallCleansOnlyStrictInterruptedStagingDirectories()
    {
        using var folder = new TempFolder();
        var stale = Path.Combine(folder.Path, "en-ru.download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "partial.onnx"), "interrupted download");
        var unrelated = Path.Combine(folder.Path, "en-ru.download-user-files");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "user data");
        using var manager = Manager(folder.Path, _ => Response(ModelBytes));
        await manager.InstallAsync("en", "ru", null, TestContext.Current.CancellationToken);
        Assert.False(Directory.Exists(stale));
        Assert.True(File.Exists(Path.Combine(unrelated, "keep.txt")));
        Assert.True(manager.IsInstalled("en", "ru"));
    }

    [Fact]
    public async Task UnsupportedPairFailsBeforeNetwork_AndNoModelLoadsForMissingPackage()
    {
        using var folder = new TempFolder();
        using var manager = Manager(folder.Path, _ => throw new InvalidOperationException("Must stay offline."));
        await Assert.ThrowsAsync<TranslationException>(() => manager.InstallAsync("zz", "ru", null, TestContext.Current.CancellationToken));
        using var engine = new OfflineTranslationService(manager);
        var error = await Assert.ThrowsAsync<TranslationException>(() => engine.TranslateAsync("Hello", "en", "ru", TestContext.Current.CancellationToken));
        Assert.Equal(TranslationFailure.NotConfigured, error.Failure);
    }

    private static TranslationPackageManager Manager(string root, Func<HttpRequestMessage, HttpResponseMessage> send) =>
        new(new HttpClient(new PackageHandler(send)), root, [TestModel]);

    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class PackageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = send(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
