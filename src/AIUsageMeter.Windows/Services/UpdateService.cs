using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows.Services;

/// <summary>
/// Checks GitHub for a newer release and, when the user asks for it, installs one.
///
/// The download is verified against the <c>SHA256SUMS-windows-*.txt</c> published beside it before
/// anything is executed. The installer then runs silently: it carries the same fixed AppId as the
/// one already installed, so it upgrades in place rather than landing beside it, and it is a
/// per-user install, so it still asks for no administrator rights.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    /// <summary>Long enough that a machine woken from sleep is not asked to check twice in a day.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    /// <summary>The first check waits for the opening refresh to finish rather than racing it.</summary>
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(10);
    /// <summary>A self-contained build is around 70 MB; this only stops an unbounded write.</summary>
    private const long MaximumInstallerBytes = 400L * 1024 * 1024;
    private const int MaximumChecksumBytes = 64 * 1024;

    private readonly Dispatcher _dispatcher;
    private readonly HttpClient _client;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _work;
    private int _checking;
    private bool _disposed;

    public UpdateService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        // Its own client rather than the shared BoundedHttpClient: that one caps a response at 2 MB
        // and refuses redirects, and an asset download is large and always redirected to a CDN.
        _client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            UseCookies = false
        }, true)
        {
            // Generous: this covers a whole installer download, not one request/response pair.
            Timeout = TimeSpan.FromMinutes(10)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"AIUsageMeter-Windows/{CurrentVersion}");
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = FirstDelay };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = Interval;
            _ = CheckAsync(quiet: true);
        };
    }

    public UpdateState State { get; private set; } = new();
    public event EventHandler? StateChanged;

    /// <summary>
    /// The running build's version. Read from the assembly rather than a constant so it can only
    /// ever be the version that was actually built; the informational version is skipped because
    /// SourceLink appends a commit hash to it.
    /// </summary>
    public static ReleaseVersion CurrentVersion { get; } =
        ReleaseVersion.Parse(Assembly.GetEntryAssembly()?.GetName().Version?.ToString()) ?? new ReleaseVersion(0, 0, 0);

    public static UpdateTarget Target => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? UpdateTarget.WindowsArm64
        : UpdateTarget.WindowsX64;

    public void Start() => _timer.Start();

    /// <summary>
    /// Asks GitHub what the newest release is. <paramref name="quiet"/> is the scheduled check: it
    /// leaves the pane alone rather than reporting that a background poll found no network.
    /// </summary>
    public async Task CheckAsync(bool quiet = false)
    {
        if (State.IsBusy || Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;
        Cancel();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        if (!quiet) Publish(new UpdateState(UpdateStage.Checking));

        try
        {
            using var request = ReleaseFeed.Request();
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await ReadAsync(response, ReleaseFeed.MaximumBytes, token).ConfigureAwait(false);

            var package = UpdateCheck.Evaluate(CurrentVersion, ReleaseFeed.Parse(body), Target);
            Publish(package is null ? new UpdateState(UpdateStage.UpToDate) : new UpdateState(UpdateStage.Available, package));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is HttpRequestException or IOException or UsageMeterException or InvalidDataException)
        {
            // A failed check is not a failed update: nothing was promised, so say nothing on the
            // scheduled poll and keep whatever the pane already showed.
            if (!quiet) Publish(new UpdateState(UpdateStage.Failed, null, 0, "Could not reach GitHub to check for updates."));
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    /// <summary>Downloads the offered package, verifies it, and hands over to the installer.</summary>
    public async Task InstallAsync()
    {
        if (Volatile.Read(ref _checking) != 0 || State.Package is not { } package || State.IsBusy) return;
        Cancel();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        Publish(new UpdateState(UpdateStage.Downloading, package));

        try
        {
            var digest = await DigestAsync(package, token).ConfigureAwait(false);
            var installer = await DownloadAsync(package, digest, token).ConfigureAwait(false);
            Publish(new UpdateState(UpdateStage.Ready, package, 1));
            Launch(installer);
        }
        catch (OperationCanceledException) { }
        catch (InvalidDataException error) { Publish(new UpdateState(UpdateStage.Failed, package, 0, error.Message)); }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Publish(new UpdateState(UpdateStage.Failed, package, 0, "The update could not be downloaded. Try again later."));
        }
    }

    /// <summary>Reads the digest the release publishes for the installer we are about to fetch.</summary>
    private async Task<string> DigestAsync(UpdatePackage package, CancellationToken token)
    {
        using var response = await _client.GetAsync(package.Checksums.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = Encoding.UTF8.GetString(await ReadAsync(response, MaximumChecksumBytes, token).ConfigureAwait(false));
        return ChecksumFile.DigestFor(text, package.Installer.Name)
            ?? throw new InvalidDataException("The release does not publish a checksum for this installer.");
    }

    /// <summary>
    /// Streams the installer to disk, hashing as it goes, and refuses to keep a file whose digest
    /// does not match. Nothing is executed before this returns.
    /// </summary>
    private async Task<string> DownloadAsync(UpdatePackage package, string digest, CancellationToken token)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsageMeter", "updates");
        Directory.CreateDirectory(directory);
        Sweep(directory, package.Installer.Name);
        var path = Path.Combine(directory, package.Installer.Name);

        // An interrupted attempt may have left a good copy behind; hashing it beats fetching 70 MB again.
        if (File.Exists(path) && await MatchesAsync(path, digest, token).ConfigureAwait(false)) return path;

        using var response = await _client.GetAsync(package.Installer.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var expected = response.Content.Headers.ContentLength;
        if (expected > MaximumInstallerBytes) throw new InvalidDataException("The published installer is larger than expected.");

        var temporary = path + ".part";
        using (var hash = SHA256.Create())
        await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
        {
            var buffer = new byte[128 * 1024];
            long written = 0;
            var shown = -1;
            while (true)
            {
                var count = await input.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0) break;
                written += count;
                if (written > MaximumInstallerBytes) throw new InvalidDataException("The download grew past the size limit and was stopped.");
                hash.TransformBlock(buffer, 0, count, null, 0);
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                if (expected is not > 0) continue;
                // One post per whole percent. Per chunk would be several hundred dispatcher hops
                // for a reading that cannot change visibly between them.
                var fraction = Math.Min(1, (double)written / expected.Value);
                var percent = (int)(fraction * 100);
                if (percent == shown) continue;
                shown = percent;
                Publish(new UpdateState(UpdateStage.Downloading, package, fraction));
            }
            hash.TransformFinalBlock([], 0, 0);
            if (!Matches(hash.Hash, digest))
            {
                // Left on disk it would be an unverified executable in a directory we later run from.
                output.Close();
                Delete(temporary);
                throw new InvalidDataException("The download did not match the checksum GitHub published. Nothing was installed.");
            }
        }

        File.Move(temporary, path, true);
        return path;
    }

    /// <summary>
    /// Starts the installer and stands aside. The app has to be gone before its own executable is
    /// replaced: Restart Manager would otherwise ask the overlay to close, and the overlay refuses
    /// on purpose so that closing it only hides it.
    /// </summary>
    private void Launch(string installer)
    {
        try
        {
            Process.Start(new ProcessStartInfo(installer)
            {
                // /UPDATE is this project's own flag: it tells the silent install to start the app
                // again afterwards, which a plain silent install must not do.
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE",
                UseShellExecute = true
            });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException)
        {
            Publish(new UpdateState(UpdateStage.Failed, State.Package, 0, "The installer would not start."));
            return;
        }

        Publish(new UpdateState(UpdateStage.Installing, State.Package, 1));
        // Inno spends its first moment unpacking itself, so quitting now leaves the executable free
        // by the time it reaches the file copy. The timer is created and started on the dispatcher:
        // this method runs on a pool thread once the download has been awaited, and a DispatcherTimer
        // may only be started from the thread it belongs to.
        _ = _dispatcher.InvokeAsync(() =>
        {
            var exit = new DispatcherTimer(DispatcherPriority.Send, _dispatcher) { Interval = TimeSpan.FromMilliseconds(800) };
            exit.Tick += (_, _) => { exit.Stop(); System.Windows.Application.Current?.Shutdown(); };
            exit.Start();
        });
    }

    /// <summary>Clears installers left by earlier updates, so the folder holds one file at most.</summary>
    private static void Sweep(string directory, string keep)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
                if (!string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase)) Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static async Task<bool> MatchesAsync(string path, string digest, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Matches(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false), digest);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool Matches(byte[]? hash, string digest) =>
        hash is not null && Convert.ToHexString(hash).Equals(digest, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("The response was larger than expected.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(limit, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await input.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > limit) throw new InvalidDataException("The response was larger than expected.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private void Publish(UpdateState state)
    {
        void Apply()
        {
            if (_disposed) return;
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_dispatcher.CheckAccess()) Apply();
        else _dispatcher.Invoke(Apply);
    }

    private void Cancel() { _work?.Cancel(); _work?.Dispose(); _work = null; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Cancel();
        _client.Dispose();
    }
}
