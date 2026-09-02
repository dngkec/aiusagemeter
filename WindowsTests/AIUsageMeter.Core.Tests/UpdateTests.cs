using System.Text;
using AIUsageMeter.Core;

namespace AIUsageMeter.Core.Tests;

[TestClass]
public sealed class UpdateTests
{
    private static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);
    private static ReleaseVersion V(string text) => ReleaseVersion.Parse(text) ?? throw new AssertFailedException($"'{text}' did not parse.");

    private static string Feed(string tag, params string[] assets)
    {
        var entries = assets.Select(name =>
            $$"""{"name":"{{name}}","browser_download_url":"https://github.com/dngkec/aiusagemeter/releases/download/{{tag}}/{{name}}"}""");
        return $$"""
            {"tag_name":"{{tag}}","draft":false,"prerelease":false,
             "html_url":"https://github.com/dngkec/aiusagemeter/releases/tag/{{tag}}",
             "assets":[{{string.Join(",", entries)}}]}
            """;
    }

    [TestMethod]
    public void VersionParsesBothTagAndBundleSpellings()
    {
        Assert.AreEqual(new ReleaseVersion(1, 2, 0), V("v1.2.0"));
        Assert.AreEqual(new ReleaseVersion(1, 2, 0), V("1.2.0"));
        Assert.AreEqual(new ReleaseVersion(1, 2, 0), V("1.2"));
        Assert.AreEqual(new ReleaseVersion(1, 2, 0), V(" V1.2.0 "));
        // .NET writes four components into an assembly version; releases are tagged on three.
        Assert.AreEqual(new ReleaseVersion(1, 2, 3), V("1.2.3.0"));
        Assert.AreEqual(new ReleaseVersion(1, 2, 0), ReleaseVersion.ParseReleaseTag("v1.2.0"));
    }

    [TestMethod]
    public void VersionRefusesAnythingItCannotOrder()
    {
        Assert.IsNull(ReleaseVersion.Parse("1.3.0-beta.1"));
        Assert.IsNull(ReleaseVersion.Parse("nightly"));
        Assert.IsNull(ReleaseVersion.Parse("1"));
        Assert.IsNull(ReleaseVersion.Parse("1.-2.0"));
        Assert.IsNull(ReleaseVersion.Parse("1.2.0.0.1"));
        Assert.IsNull(ReleaseVersion.Parse(""));
        Assert.IsNull(ReleaseVersion.Parse(null));
    }

    [TestMethod]
    public void VersionComparesNumericallyNotAsText()
    {
        Assert.IsTrue(V("1.9.0") < V("1.10.0"));
        Assert.IsTrue(V("2.0.0") > V("1.99.99"));
        Assert.IsTrue(V("1.2.0") <= V("1.2.0"));
        Assert.IsTrue(V("1.2.1") > V("1.2.0"));
    }

    [TestMethod]
    public void FeedReadsTagAssetsAndPage()
    {
        var release = ReleaseFeed.Parse(Json(Feed("v1.2.0", "AIUsageMeter-1.2.0.dmg", "SHA256SUMS-macos.txt")));
        Assert.IsNotNull(release);
        Assert.AreEqual(new ReleaseVersion(1, 2, 0), release.Version);
        Assert.AreEqual(2, release.Assets.Count);
        Assert.AreEqual("https://github.com/dngkec/aiusagemeter/releases/tag/v1.2.0", release.Page?.ToString());
    }

    [TestMethod]
    public void FeedIgnoresDraftsPrereleasesAndUnorderableTags()
    {
        Assert.IsNull(ReleaseFeed.Parse(Json("""{"tag_name":"v1.2.0","draft":true,"assets":[]}""")));
        Assert.IsNull(ReleaseFeed.Parse(Json("""{"tag_name":"v1.2.0","prerelease":true,"assets":[]}""")));
        Assert.IsNull(ReleaseFeed.Parse(Json("""{"tag_name":"nightly","assets":[]}""")));
        Assert.IsNull(ReleaseFeed.Parse(Json("""{"tag_name":"v1.2","assets":[]}""")));
        Assert.IsNull(ReleaseFeed.Parse(Json("""{"tag_name":"v1.2.0.0","assets":[]}""")));
        Assert.IsNull(ReleaseFeed.Parse(Json("""{"assets":[]}""")));
    }

    [TestMethod]
    public void FeedAllowsOnlyThisRepositoriesReleaseUrls()
    {
        var release = ReleaseFeed.Parse(Json("""
            {"tag_name":"v1.2.0","assets":[
              {"name":"good.dmg","browser_download_url":"https://github.com/dngkec/aiusagemeter/releases/download/v1.2.0/good.dmg"},
              {"name":"other-repo.dmg","browser_download_url":"https://github.com/other/aiusagemeter/releases/download/v1.2.0/other-repo.dmg"},
              {"name":"wrong-tag.dmg","browser_download_url":"https://github.com/dngkec/aiusagemeter/releases/download/v9.9.9/wrong-tag.dmg"},
              {"name":"external.dmg","browser_download_url":"https://example.com/external.dmg"},
              {"name":"insecure.dmg","browser_download_url":"http://example.com/insecure.dmg"},
              {"name":"nameless"},
              {"browser_download_url":"https://example.com/anonymous.dmg"}]}
            """));
        Assert.IsNotNull(release);
        CollectionAssert.AreEqual(new[] { "good.dmg" }, release.Assets.Select(x => x.Name).ToArray());
    }

    [TestMethod]
    public void FeedAllowsOnlyThisRepositoriesReleasePage()
    {
        var trusted = ReleaseFeed.Parse(Json(Feed("v1.2.0")));
        Assert.AreEqual("https://github.com/dngkec/aiusagemeter/releases/tag/v1.2.0", trusted?.Page?.ToString());

        var external = ReleaseFeed.Parse(Json("""
            {"tag_name":"v1.2.0","html_url":"https://example.com/releases/tag/v1.2.0","assets":[]}
            """));
        Assert.IsNotNull(external);
        Assert.IsNull(external.Page);
    }

    [TestMethod]
    public void EvaluatePicksThePackageForThisPlatform()
    {
        var release = ReleaseFeed.Parse(Json(Feed("v1.2.0",
            "AIUsageMeter-1.2.0.dmg", "SHA256SUMS-macos.txt",
            "AIUsageMeter-1.2.0-win-x64-setup.exe", "SHA256SUMS-windows-win-x64.txt",
            "AIUsageMeter-1.2.0-win-arm64-setup.exe", "SHA256SUMS-windows-win-arm64.txt")));

        var x64 = UpdateCheck.Evaluate(V("1.1.0"), release, UpdateTarget.WindowsX64);
        Assert.AreEqual("AIUsageMeter-1.2.0-win-x64-setup.exe", x64?.Installer.Name);
        Assert.AreEqual("SHA256SUMS-windows-win-x64.txt", x64?.Checksums.Name);

        var arm = UpdateCheck.Evaluate(V("1.1.0"), release, UpdateTarget.WindowsArm64);
        Assert.AreEqual("AIUsageMeter-1.2.0-win-arm64-setup.exe", arm?.Installer.Name);

        var mac = UpdateCheck.Evaluate(V("1.1.0"), release, UpdateTarget.MacOS);
        Assert.AreEqual("AIUsageMeter-1.2.0.dmg", mac?.Installer.Name);
        Assert.AreEqual("SHA256SUMS-macos.txt", mac?.Checksums.Name);
    }

    [TestMethod]
    public void EvaluateOffersNothingWhenTheAppIsCurrentOrAhead()
    {
        var release = ReleaseFeed.Parse(Json(Feed("v1.2.0", "AIUsageMeter-1.2.0.dmg", "SHA256SUMS-macos.txt")));
        Assert.IsNull(UpdateCheck.Evaluate(V("1.2.0"), release, UpdateTarget.MacOS));
        Assert.IsNull(UpdateCheck.Evaluate(V("1.3.0"), release, UpdateTarget.MacOS));
        Assert.IsNull(UpdateCheck.Evaluate(V("1.1.0"), null, UpdateTarget.MacOS));
    }

    [TestMethod]
    public void EvaluateRefusesAReleaseItCannotVerifyOrThatMissesThisPlatform()
    {
        var unverifiable = ReleaseFeed.Parse(Json(Feed("v1.2.0", "AIUsageMeter-1.2.0.dmg")));
        Assert.IsNull(UpdateCheck.Evaluate(V("1.1.0"), unverifiable, UpdateTarget.MacOS));

        var macOnly = ReleaseFeed.Parse(Json(Feed("v1.2.0", "AIUsageMeter-1.2.0.dmg", "SHA256SUMS-macos.txt")));
        Assert.IsNull(UpdateCheck.Evaluate(V("1.1.0"), macOnly, UpdateTarget.WindowsX64));
    }

    /// <summary>
    /// The shape GitHub actually returns, trimmed from the live response for this repository:
    /// twenty fields we ignore, a release <c>name</c> that must not replace <c>tag_name</c>, and
    /// assets carrying a dozen keys of their own.
    /// </summary>
    [TestMethod]
    public void FeedReadsTheShapeGitHubActuallyReturns()
    {
        var release = ReleaseFeed.Parse(Json("""
            {"url":"https://api.github.com/repos/dngkec/aiusagemeter/releases/253290612",
             "assets_url":"https://api.github.com/repos/dngkec/aiusagemeter/releases/253290612/assets",
             "upload_url":"https://uploads.github.com/repos/dngkec/aiusagemeter/releases/253290612/assets{?name,label}",
             "html_url":"https://github.com/dngkec/aiusagemeter/releases/tag/v1.1.0",
             "id":253290612,"author":{"login":"github-actions[bot]","id":41898282,"type":"Bot"},
             "node_id":"RE_kwDOP","tag_name":"v1.1.0","target_commitish":"master","name":"v1.1.0",
             "draft":false,"immutable":false,"prerelease":false,
             "created_at":"2026-09-01T18:19:52Z","published_at":"2026-09-01T18:23:10Z",
             "assets":[
               {"url":"https://api.github.com/repos/dngkec/aiusagemeter/releases/assets/1","id":1,
                "node_id":"RA_kwD","name":"AIUsageMeter-1.1.0-win-x64-setup.exe","label":null,
                "uploader":{"login":"github-actions[bot]","id":41898282},
                "content_type":"application/x-msdownload","state":"uploaded","size":74183291,
                "digest":"sha256:9736546db4fe6db0742aa37443da29d9f089d22866d05a21b3696da4fd8db789",
                "download_count":0,"created_at":"2026-09-01T18:23:07Z","updated_at":"2026-09-01T18:23:09Z",
                "browser_download_url":"https://github.com/dngkec/aiusagemeter/releases/download/v1.1.0/AIUsageMeter-1.1.0-win-x64-setup.exe"},
               {"url":"https://api.github.com/repos/dngkec/aiusagemeter/releases/assets/2","id":2,
                "name":"SHA256SUMS-windows-win-x64.txt","label":null,"content_type":"text/plain",
                "state":"uploaded","size":86,
                "browser_download_url":"https://github.com/dngkec/aiusagemeter/releases/download/v1.1.0/SHA256SUMS-windows-win-x64.txt"}],
             "tarball_url":"https://api.github.com/repos/dngkec/aiusagemeter/tarball/v1.1.0",
             "zipball_url":"https://api.github.com/repos/dngkec/aiusagemeter/zipball/v1.1.0",
             "body":"## Windows\n\n- Native WPF app reaches parity with the macOS build."}
            """));

        Assert.IsNotNull(release);
        Assert.AreEqual(new ReleaseVersion(1, 1, 0), release.Version);
        Assert.AreEqual("https://github.com/dngkec/aiusagemeter/releases/tag/v1.1.0", release.Page?.ToString());

        // The install this ships to is already 1.1.0, which is the whole point of the comparison.
        Assert.IsNull(UpdateCheck.Evaluate(V("1.1.0"), release, UpdateTarget.WindowsX64));

        var package = UpdateCheck.Evaluate(V("1.0.0"), release, UpdateTarget.WindowsX64);
        Assert.IsNotNull(package);
        Assert.AreEqual("https://github.com/dngkec/aiusagemeter/releases/download/v1.1.0/AIUsageMeter-1.1.0-win-x64-setup.exe",
            package.Installer.Url.ToString());
        Assert.AreEqual("https://github.com/dngkec/aiusagemeter/releases/download/v1.1.0/SHA256SUMS-windows-win-x64.txt",
            package.Checksums.Url.ToString());
    }

    /// <summary>The file the release publishes, in the exact form the build script writes it.</summary>
    [TestMethod]
    public void ChecksumFileReadsThePublishedListing()
    {
        const string published = "9736546db4fe6db0742aa37443da29d9f089d22866d05a21b3696da4fd8db789  AIUsageMeter-1.1.0-win-x64-setup.exe";
        Assert.AreEqual("9736546db4fe6db0742aa37443da29d9f089d22866d05a21b3696da4fd8db789",
            ChecksumFile.DigestFor(published, "AIUsageMeter-1.1.0-win-x64-setup.exe"));
    }

    [TestMethod]
    public void ChecksumFileFindsTheDigestForOneNamedFile()
    {
        const string listing = """
            e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  AIUsageMeter-1.2.0.dmg
            0000000000000000000000000000000000000000000000000000000000000000 *AIUsageMeter-1.2.0-win-x64-setup.exe
            """;
        Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ChecksumFile.DigestFor(listing, "AIUsageMeter-1.2.0.dmg"));
        Assert.AreEqual("0000000000000000000000000000000000000000000000000000000000000000",
            ChecksumFile.DigestFor(listing, "AIUsageMeter-1.2.0-win-x64-setup.exe"));
        Assert.IsNull(ChecksumFile.DigestFor(listing, "AIUsageMeter-1.3.0.dmg"));
    }

    [TestMethod]
    public void ChecksumFileToleratesWindowsLineEndingsAndUppercaseHex()
    {
        const string listing = "# generated\r\nE3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  setup.exe\r\n";
        Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ChecksumFile.DigestFor(listing, "setup.exe"));
    }

    [TestMethod]
    public void ChecksumFileRejectsLinesThatAreNotASha256Digest()
    {
        Assert.IsNull(ChecksumFile.DigestFor("deadbeef  setup.exe", "setup.exe"));
        Assert.IsNull(ChecksumFile.DigestFor("not-hex-but-exactly-sixty-four-characters-long-xxxxxxxxxxxxxxxxx  setup.exe", "setup.exe"));
        Assert.IsNull(ChecksumFile.DigestFor("setup.exe", "setup.exe"));
    }

    [TestMethod]
    public void StateDescribesEachStageForTheAboutPaneAndTheMenu()
    {
        var package = new UpdatePackage(V("1.2.0"),
            new ReleaseAsset("AIUsageMeter-1.2.0-win-x64-setup.exe", new Uri("https://example.com/a")),
            new ReleaseAsset("SHA256SUMS-windows-win-x64.txt", new Uri("https://example.com/b")), null);

        Assert.AreEqual("AIUsageMeter is up to date.", new UpdateState(UpdateStage.UpToDate).Summary);
        Assert.IsNull(new UpdateState(UpdateStage.UpToDate).MenuTitle);

        var available = new UpdateState(UpdateStage.Available, package);
        Assert.AreEqual("Version 1.2.0 is available.", available.Summary);
        Assert.AreEqual("Update to 1.2.0…", available.MenuTitle);
        Assert.IsTrue(available.CanInstall);

        var downloading = new UpdateState(UpdateStage.Downloading, package, 0.42);
        Assert.AreEqual("Downloading version 1.2.0… 42%", downloading.Summary);
        Assert.IsTrue(downloading.IsBusy);
        Assert.IsFalse(downloading.CanInstall);

        // A failure keeps the package, so the button stays and the user can try again.
        var failed = new UpdateState(UpdateStage.Failed, package, 0, "The download did not match its checksum.");
        Assert.AreEqual("The download did not match its checksum.", failed.Summary);
        Assert.IsTrue(failed.CanInstall);

        // Installing is under way; offering it again from the menu would start a second installer.
        Assert.IsNull(new UpdateState(UpdateStage.Installing, package).MenuTitle);
    }
}
