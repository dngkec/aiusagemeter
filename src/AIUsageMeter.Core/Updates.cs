using System.Globalization;
using System.Text.Json.Nodes;

namespace AIUsageMeter.Core;

/// <summary>
/// A release version, as far as this app cares: three numbers.
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    /// <summary>
    /// Parses an installed bundle or assembly version. A fourth component is ignored because .NET
    /// writes one; release tags use the stricter <see cref="ParseReleaseTag"/> parser below.
    /// </summary>
    public static ReleaseVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        if (value.Length == 0) return null;

        return ParseComponents(value, 2, 4);
    }

    /// <summary>Release tags must be exactly <c>v1.2.0</c> or <c>1.2.0</c>.</summary>
    public static ReleaseVersion? ParseReleaseTag(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        return ParseComponents(value, 3, 3);
    }

    private static ReleaseVersion? ParseComponents(string value, int minimum, int maximum)
    {
        if (value.Length == 0) return null;
        var parts = value.Split('.');
        if (parts.Length < minimum || parts.Length > maximum) return null;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0) return null;
            if (i < 3) numbers[i] = number;
        }
        return new ReleaseVersion(numbers[0], numbers[1], numbers[2]);
    }

    public int CompareTo(ReleaseVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        return Patch.CompareTo(other.Patch);
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>One downloadable file attached to a release.</summary>
public sealed record ReleaseAsset(string Name, Uri Url);

/// <summary>A published release, reduced to what an update needs.</summary>
public sealed record Release(ReleaseVersion Version, IReadOnlyList<ReleaseAsset> Assets, Uri? Page);

/// <summary>
/// Which package this build should install. The names mirror what
/// <c>scripts/package-windows.ps1</c> and <c>scripts/make-dmg.sh</c> write into <c>dist/</c>.
/// </summary>
public enum UpdateTarget { WindowsX64, WindowsArm64, MacOS }

/// <summary>The verified download plus the checksum file that vouches for it.</summary>
public sealed record UpdatePackage(ReleaseVersion Version, ReleaseAsset Installer, ReleaseAsset Checksums, Uri? Page);

public static class ReleaseFeed
{
    /// <summary>
    /// The releases endpoint for this repository. <c>/releases/latest</c> skips drafts and
    /// prereleases for us, so a draft cut in preparation never reaches an installed app.
    /// </summary>
    public static readonly Uri Latest = new("https://api.github.com/repos/dngkec/aiusagemeter/releases/latest");

    /// <summary>The response is small; anything larger than this is not the feed we asked for.</summary>
    public const int MaximumBytes = 512 * 1024;

    public static HttpRequestMessage Request()
    {
        var request = RequestFactory.Create(Latest, headers: new Dictionary<string, string>
        {
            ["Accept"] = "application/vnd.github+json",
            ["X-GitHub-Api-Version"] = "2022-11-28"
        });
        return request;
    }

    /// <summary>
    /// Reads the release GitHub returns. A tag that is not three numbers — someone tagging by
    /// hand, or a prerelease slipping through — is reported as "no release" rather than as an
    /// update, because an unorderable version cannot be compared against the one installed.
    /// </summary>
    public static Release? Parse(ReadOnlyMemory<byte> data)
    {
        var root = Json.Parse(data);
        if (root.Text("tag_name") is not { } tag) return null;
        if (ReleaseVersion.ParseReleaseTag(tag) is not { } version) return null;
        if (root.Flag("draft") == true || root.Flag("prerelease") == true) return null;

        var assets = new List<ReleaseAsset>();
        foreach (var node in root.At("assets").Array())
        {
            if (node.Text("name") is not { } name || string.IsNullOrWhiteSpace(name)) continue;
            if (node.Text("browser_download_url") is not { } href) continue;
            if (!TrustedAssetUrl(href, tag, name, out var url)) continue;
            assets.Add(new ReleaseAsset(name, url));
        }

        var page = root.Text("html_url") is { } link && TrustedReleasePage(link, tag, out var pageUrl)
            ? pageUrl
            : null;
        return new Release(version, assets, page);
    }

    private static bool TrustedAssetUrl(string value, string tag, string name, out Uri url)
    {
        if (!TrustedGitHub(value, out url)) return false;
        var path = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        return path.SequenceEqual(["dngkec", "aiusagemeter", "releases", "download", tag, name]);
    }

    private static bool TrustedReleasePage(string value, string tag, out Uri url)
    {
        if (!TrustedGitHub(value, out url)) return false;
        var path = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        return path.SequenceEqual(["dngkec", "aiusagemeter", "releases", "tag", tag]);
    }

    private static bool TrustedGitHub(string value, out Uri url)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            && parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && parsed.IsDefaultPort
            && string.IsNullOrEmpty(parsed.UserInfo)
            && string.IsNullOrEmpty(parsed.Query)
            && string.IsNullOrEmpty(parsed.Fragment);
        url = parsed ?? new Uri("https://github.com/");
        return valid;
    }
}

public static class UpdateCheck
{
    /// <summary>
    /// Decides whether <paramref name="release"/> is worth installing over <paramref name="installed"/>,
    /// and finds the two assets the install needs. Returns null when the app is current, when the
    /// release carries no package for this platform, or when the package has no checksum file —
    /// an unverifiable download is not offered at all.
    /// </summary>
    public static UpdatePackage? Evaluate(ReleaseVersion installed, Release? release, UpdateTarget target)
    {
        if (release is null || release.Version <= installed) return null;
        var installer = Find(release.Assets, InstallerName(release.Version, target));
        var checksums = Find(release.Assets, ChecksumName(target));
        if (installer is null || checksums is null) return null;
        return new UpdatePackage(release.Version, installer, checksums, release.Page);
    }

    public static string InstallerName(ReleaseVersion version, UpdateTarget target) => target switch
    {
        UpdateTarget.WindowsX64 => $"AIUsageMeter-{version}-win-x64-setup.exe",
        UpdateTarget.WindowsArm64 => $"AIUsageMeter-{version}-win-arm64-setup.exe",
        _ => $"AIUsageMeter-{version}.dmg"
    };

    public static string ChecksumName(UpdateTarget target) => target switch
    {
        UpdateTarget.WindowsX64 => "SHA256SUMS-windows-win-x64.txt",
        UpdateTarget.WindowsArm64 => "SHA256SUMS-windows-win-arm64.txt",
        _ => "SHA256SUMS-macos.txt"
    };

    private static ReleaseAsset? Find(IReadOnlyList<ReleaseAsset> assets, string name)
        => assets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
}

public static class ChecksumFile
{
    /// <summary>
    /// Pulls one file's digest out of a <c>shasum</c>-style listing: <c>&lt;hex&gt;  &lt;name&gt;</c>,
    /// one per line, with the binary marker <c>*</c> allowed before the name. Returns lowercase
    /// hex, or null when the file does not vouch for <paramref name="fileName"/>.
    /// </summary>
    public static string? DigestFor(string text, string fileName)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            var split = trimmed.IndexOfAny([' ', '\t']);
            if (split <= 0) continue;

            var digest = trimmed[..split];
            if (digest.Length != 64 || !digest.All(Uri.IsHexDigit)) continue;

            var name = trimmed[(split + 1)..].TrimStart(' ', '\t', '*');
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return digest.ToLowerInvariant();
        }
        return null;
    }
}

/// <summary>What the About pane and the tray menu show, and nothing more.</summary>
public enum UpdateStage { Idle, Checking, UpToDate, Available, Downloading, Ready, Installing, Failed }

public sealed record UpdateState(
    UpdateStage Stage = UpdateStage.Idle,
    UpdatePackage? Package = null,
    double Progress = 0,
    string? Message = null)
{
    public bool HasUpdate => Package is not null && Stage is UpdateStage.Available or UpdateStage.Downloading or UpdateStage.Ready or UpdateStage.Installing;
    public bool CanInstall => Package is not null && Stage is UpdateStage.Available or UpdateStage.Failed;
    public bool IsBusy => Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Installing;

    /// <summary>The one line of status the About pane shows under the version.</summary>
    public string Summary => Stage switch
    {
        UpdateStage.Checking => "Checking for updates…",
        UpdateStage.UpToDate => "AIUsageMeter is up to date.",
        UpdateStage.Available => $"Version {Package?.Version} is available.",
        UpdateStage.Downloading => $"Downloading version {Package?.Version}… {Progress * 100:0}%",
        UpdateStage.Ready => $"Version {Package?.Version} is ready to install.",
        UpdateStage.Installing => "Installing. AIUsageMeter will restart.",
        UpdateStage.Failed => Message ?? "The update could not be installed.",
        _ => ""
    };

    /// <summary>The tray and menu-bar entry, present only while there is something to install.</summary>
    public string? MenuTitle => HasUpdate && Stage != UpdateStage.Installing ? $"Update to {Package!.Version}…" : null;
}
