using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PageCollector.Core.Tests;

[TestClass]
public sealed class EngineInstallerTests : TestWorkspace
{
    private static readonly byte[] OlderBytes = Encoding.UTF8.GetBytes("older verified executable");
    private static readonly byte[] LatestBytes = Encoding.UTF8.GetBytes("newer verified executable");
    private static readonly EngineRelease Older = Release("2.9.0", OlderBytes);
    private static readonly EngineRelease Latest = Release("2.10.0", LatestBytes);

    [TestMethod]
    public async Task ReleasesArePaginatedFilteredAndSortedNumericallyAsync()
    {
        using var handler = new StubHandler((request, _) =>
        {
            var items = request.RequestUri!.Query.EndsWith("page=1", StringComparison.Ordinal)
                ? Enumerable.Repeat(ReleaseJson(Older), 100).ToArray()
                : new[]
                {
                    ReleaseJson(Latest),
                    ReleaseJson(Older with { Version = "9.0.0" }, prerelease: true),
                    ReleaseJson(Older with { Version = "8.0.0" }, draft: true),
                    ReleaseJson(Older with { Version = "../escape" }),
                    ReleaseJson(Older with { Sha256 = "invalid" }),
                    ReleaseJson(Older, assetName: "single-file"),
                    ReleaseJson(Older, url: "https://example.com/untrusted.exe"),
                    ReleaseJson(Older, digest: null)
                };

            return Task.FromResult(JsonResponse(items));
        });
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        var state = await installer.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(2, handler.Requests.Count);
        CollectionAssert.AreEqual(new[] { Latest.Version, Older.Version }, state.Releases.Select(release => release.Version).ToArray());
        Assert.AreEqual(Latest.Version, state.LatestVersion);
        Assert.IsNull(state.ActiveRelease);
        Assert.IsNull(state.PreferredVersion);
        Assert.IsTrue(handler.Requests.All(uri => uri.Host == "api.github.com"));
    }

    [TestMethod]
    public async Task FirstUseInstallsLatestAndExplicitOlderChoiceSurvivesRestartOfflineAsync()
    {
        var online = true;
        using var handler = new StubHandler((request, _) =>
        {
            if (!online)
            {
                throw new HttpRequestException("Offline");
            }

            return Task.FromResult(StandardResponse(request));
        });
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        var first = await installer.EnsureInstalledAsync(null, CancellationToken.None);

        Assert.AreEqual(Latest.Version, first.Version);
        Assert.IsNull(installer.ReadState().PreferredVersion);
        Assert.IsTrue(await EngineInstaller.IsValidAsync(first.FilePath, Latest, CancellationToken.None));
        await installer.InstallAsync(Older.Version, null, CancellationToken.None);
        online = false;
        var requestCount = handler.Requests.Count;
        var restarted = new EngineInstaller(Root, client);
        var active = await restarted.EnsureInstalledAsync(null, CancellationToken.None);

        Assert.AreEqual(Older.Version, active.Version);
        Assert.AreEqual(Older.Version, restarted.ReadState().PreferredVersion);
        Assert.AreEqual(requestCount, handler.Requests.Count);
        Assert.IsTrue(File.Exists(first.FilePath));

        // Rollback to an already installed release also works offline without another download.
        var previous = await restarted.InstallAsync(Latest.Version, null, CancellationToken.None);

        Assert.AreEqual(first, previous);
        Assert.AreEqual(requestCount, handler.Requests.Count);
    }

    [TestMethod]
    public async Task CheckingUpdatesPreservesActiveVersionAndRetainsInstalledOlderReleasesAsync()
    {
        var onlyLatest = false;
        using var handler = new StubHandler((request, _) => Task.FromResult(
            onlyLatest && request.RequestUri!.Host == "api.github.com"
                ? JsonResponse(new[] { ReleaseJson(Latest) })
                : StandardResponse(request)));
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        await installer.InstallAsync(Older.Version, null, CancellationToken.None);
        onlyLatest = true;
        var state = await installer.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(Older.Version, state.ActiveRelease?.Version);
        Assert.AreEqual(Older.Version, state.PreferredVersion);
        Assert.AreEqual(Latest.Version, state.LatestVersion);
        Assert.IsTrue(state.Releases.Contains(Older));
        Assert.IsFalse(File.Exists(Path.Combine(installer.ToolsDirectory, "single-file-" + Latest.Version + ".exe")));
        var switched = await installer.InstallAsync(null, null, CancellationToken.None);

        Assert.AreEqual(Latest.Version, switched.Version);
        Assert.IsNull(installer.ReadState().PreferredVersion);
        Assert.IsTrue(File.Exists(Path.Combine(installer.ToolsDirectory, "single-file-" + Older.Version + ".exe")));
    }

    [TestMethod]
    [DataRow("hash")]
    [DataRow("truncated")]
    [DataRow("oversized")]
    [DataRow("http")]
    public async Task FailedUpdatePreservesActiveVersionAndRemovesPartialFilesAsync(string failure)
    {
        var failDownload = false;
        using var handler = new StubHandler((request, _) =>
        {
            if (!failDownload || request.RequestUri!.AbsoluteUri != Latest.DownloadUrl)
            {
                return Task.FromResult(StandardResponse(request));
            }

            return Task.FromResult(failure switch
            {
                "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "truncated" => BytesResponse(LatestBytes[..3]),
                "oversized" => BytesResponse(new byte[LatestBytes.Length + 1]),
                _ => BytesResponse(new byte[LatestBytes.Length])
            });
        });
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        var older = await installer.InstallAsync(Older.Version, null, CancellationToken.None);
        var before = await File.ReadAllTextAsync(Path.Combine(installer.ToolsDirectory, "engine.json"));
        failDownload = true;

        if (failure == "http")
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => installer.InstallAsync(null, null, CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(null, null, CancellationToken.None));
        }

        Assert.AreEqual(before, await File.ReadAllTextAsync(Path.Combine(installer.ToolsDirectory, "engine.json")));
        CollectionAssert.AreEqual(OlderBytes, await File.ReadAllBytesAsync(older.FilePath));
        Assert.IsFalse(File.Exists(Path.Combine(installer.ToolsDirectory, "single-file-" + Latest.Version + ".exe")));
        Assert.IsEmpty(Directory.GetFiles(installer.ToolsDirectory, "*.download"));
        Assert.IsEmpty(Directory.GetFiles(installer.ToolsDirectory, "*.tmp"));
    }

    [TestMethod]
    public async Task CancelledDownloadLeavesPreviousInstallationAndCleansTemporaryFileAsync()
    {
        using var partial = new BlockingStream();
        using var handler = new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsoluteUri == Latest.DownloadUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(partial) }
                : StandardResponse(request)));
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        await installer.InstallAsync(Older.Version, null, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var install = installer.InstallAsync(null, null, cancellation.Token);

        try
        {
            await partial.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancellation.Cancel();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => install);

        Assert.AreEqual(Older.Version, installer.ReadState().ActiveRelease?.Version);
        Assert.AreEqual(Older.Version, installer.ReadState().PreferredVersion);
        Assert.IsEmpty(Directory.GetFiles(installer.ToolsDirectory, "*.download"));
        Assert.IsEmpty(Directory.GetFiles(installer.ToolsDirectory, "*.tmp"));
    }

    [TestMethod]
    public async Task FailedReleaseCheckKeepsCachedListAndWorkingInstallationAsync()
    {
        var rateLimited = false;
        using var handler = new StubHandler((request, _) => Task.FromResult(
            rateLimited ? new HttpResponseMessage(HttpStatusCode.Forbidden) : StandardResponse(request)));
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        var installed = await installer.EnsureInstalledAsync(null, CancellationToken.None);
        rateLimited = true;
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => installer.RefreshAsync(CancellationToken.None));

        Assert.Contains("GitHub", exception.Message);
        Assert.HasCount(2, installer.ReadState().Releases);
        Assert.AreEqual(installed, await installer.EnsureInstalledAsync(null, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ConnectionTimeoutIsReportedAsFailureAndKeepsActiveVersionAsync(bool releaseCheck)
    {
        var timeout = false;
        using var handler = new StubHandler((request, _) => timeout
            ? throw new OperationCanceledException("Connection timed out")
            : Task.FromResult(StandardResponse(request)));
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        await installer.InstallAsync(Older.Version, null, CancellationToken.None);
        timeout = true;

        if (releaseCheck)
        {
            await Assert.ThrowsAsync<TimeoutException>(() => installer.RefreshAsync(CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() => installer.InstallAsync(null, null, CancellationToken.None));
        }

        Assert.AreEqual(Older.Version, installer.ReadState().ActiveRelease?.Version);
        Assert.IsEmpty(Directory.GetFiles(installer.ToolsDirectory, "*.download"));
    }

    [TestMethod]
    public async Task TamperedInstalledExecutableIsVerifiedAgainAndRepairedAsync()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(StandardResponse(request)));
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        var installed = await installer.EnsureInstalledAsync(null, CancellationToken.None);
        await File.WriteAllBytesAsync(installed.FilePath, new byte[LatestBytes.Length]);
        var repaired = await installer.EnsureInstalledAsync(null, CancellationToken.None);

        Assert.AreEqual(installed, repaired);
        CollectionAssert.AreEqual(LatestBytes, await File.ReadAllBytesAsync(repaired.FilePath));
        Assert.AreEqual(2, handler.Requests.Count(uri => uri.AbsoluteUri == Latest.DownloadUrl));
    }

    [TestMethod]
    public async Task LegacyInstallationIsDiscoveredButStillRequiresVerificationAsync()
    {
        using var handler = new StubHandler((_, _) => throw new HttpRequestException("Offline"));
        using var client = new HttpClient(handler);
        var installer = new EngineInstaller(Root, client);
        Directory.CreateDirectory(installer.ToolsDirectory);
        var path = Path.Combine(installer.ToolsDirectory, "single-file-2.26.0.exe");
        await File.WriteAllTextAsync(path, "Invalid legacy executable");

        Assert.AreEqual(EngineInstaller.LegacyRelease, installer.ReadState().ActiveRelease);
        await Assert.ThrowsAsync<HttpRequestException>(() => installer.EnsureInstalledAsync(null, CancellationToken.None));

        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(EngineInstaller.LegacyRelease.DownloadUrl, handler.Requests[0].AbsoluteUri);
        Assert.IsFalse(File.Exists(Path.Combine(installer.ToolsDirectory, "engine.json")));
    }

    [TestMethod]
    public async Task InvalidCachedVersionCannotEscapeToolsDirectoryAsync()
    {
        var installer = new EngineInstaller(Root);
        Directory.CreateDirectory(installer.ToolsDirectory);
        var state = new EngineState(Older with { Version = "../escape" }, null, null, []);
        await File.WriteAllTextAsync(Path.Combine(installer.ToolsDirectory, "engine.json"), JsonSerializer.Serialize(state));

        Assert.Throws<IOException>(installer.ReadState);
        await Assert.ThrowsAsync<ArgumentException>(() => installer.InstallAsync("../escape", null, CancellationToken.None));
    }

    private static EngineRelease Release(string version, byte[] bytes) => new(version, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);

    private static object ReleaseJson(EngineRelease release, bool prerelease = false, bool draft = false, string assetName = "single-file.exe", string? url = null, string? digest = "default") => new
    {
        tag_name = "v" + release.Version,
        prerelease,
        draft,
        assets = new[]
        {
            new
            {
                name = assetName,
                browser_download_url = url ?? release.DownloadUrl,
                size = release.Size,
                digest = digest == "default" ? "sha256:" + release.Sha256 : digest
            }
        }
    };

    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
    };

    private static HttpResponseMessage StandardResponse(HttpRequestMessage request) => request.RequestUri!.Host == "api.github.com"
        ? JsonResponse(new[] { ReleaseJson(Older), ReleaseJson(Latest) })
        : BytesResponse(request.RequestUri.AbsoluteUri == Older.DownloadUrl ? OlderBytes : LatestBytes);

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.IsNotEmpty(request.Headers.UserAgent);
            Requests.Add(request.RequestUri!);

            return respond(request, cancellationToken);
        }
    }

    private sealed class BlockingStream : MemoryStream
    {
        private bool sentBytes;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!sentBytes)
            {
                sentBytes = true;
                buffer.Span[0] = 1;

                return 1;
            }

            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return 0;
        }
    }
}