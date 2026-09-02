import Foundation

/// A release version, as far as this app cares: three numbers.
public struct ReleaseVersion: Comparable, Equatable, Hashable, Sendable, CustomStringConvertible {
    public let major: Int
    public let minor: Int
    public let patch: Int

    public init(_ major: Int, _ minor: Int, _ patch: Int) {
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    /// Parses an installed bundle or assembly version. A fourth component is ignored because .NET
    /// writes one; release tags use the stricter `releaseTag(_:)` parser below.
    public init?(_ text: String?) {
        guard let version = Self.parse(text, componentCounts: 2...4) else { return nil }
        self = version
    }

    /// Release tags must be exactly `v1.2.0` or `1.2.0`; suffixes and extra components are refused.
    public static func releaseTag(_ text: String?) -> ReleaseVersion? {
        parse(text, componentCounts: 3...3)
    }

    private static func parse(_ text: String?, componentCounts: ClosedRange<Int>) -> ReleaseVersion? {
        guard var value = text?.trimmingCharacters(in: .whitespacesAndNewlines), !value.isEmpty else { return nil }
        if value.first == "v" || value.first == "V" { value.removeFirst() }
        let parts = value.split(separator: ".", omittingEmptySubsequences: false)
        guard componentCounts.contains(parts.count) else { return nil }
        var numbers = [0, 0, 0]
        for (index, part) in parts.enumerated() {
            guard !part.isEmpty, part.allSatisfy(\.isASCII), part.allSatisfy(\.isNumber), let number = Int(part) else { return nil }
            if index < 3 { numbers[index] = number }
        }
        return ReleaseVersion(numbers[0], numbers[1], numbers[2])
    }

    public static func < (lhs: ReleaseVersion, rhs: ReleaseVersion) -> Bool {
        (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
    }

    public var description: String { "\(major).\(minor).\(patch)" }
}

/// One downloadable file attached to a release.
public struct ReleaseAsset: Equatable, Sendable {
    public let name: String
    public let url: URL
    public init(name: String, url: URL) { self.name = name; self.url = url }
}

/// A published release, reduced to what an update needs.
public struct Release: Equatable, Sendable {
    public let version: ReleaseVersion
    public let assets: [ReleaseAsset]
    public let page: URL?
}

/// Which package this build should install. The names mirror what `scripts/make-dmg.sh` and
/// `scripts/package-windows.ps1` write into `dist/`.
public enum UpdateTarget: Sendable { case macOS, windowsX64, windowsArm64 }

/// The offered download plus the checksum file that vouches for it.
public struct UpdatePackage: Equatable, Sendable {
    public let version: ReleaseVersion
    public let installer: ReleaseAsset
    public let checksums: ReleaseAsset
    public let page: URL?
}

public enum ReleaseFeed {
    /// The releases endpoint for this repository. `/releases/latest` skips drafts and prereleases
    /// for us, so a draft cut in preparation never reaches an installed app.
    public static let latest = URL(string: "https://api.github.com/repos/dngkec/aiusagemeter/releases/latest")!

    /// The response is small; anything larger than this is not the feed we asked for.
    public static let maximumBytes = 512 * 1024

    public static func request() -> URLRequest {
        var request = URLRequest(url: latest)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        return request
    }

    /// Reads the release GitHub returns. A tag that is not three numbers — someone tagging by
    /// hand, or a prerelease slipping through — is reported as "no release" rather than as an
    /// update, because an unorderable version cannot be compared against the one installed.
    public static func parse(_ data: Data) -> Release? {
        guard let root = try? JSONValue.decode(data) else { return nil }
        guard let tag = root.value(at: "tag_name")?.string,
              let version = ReleaseVersion.releaseTag(tag) else { return nil }
        if root.value(at: "draft")?.boolValue == true { return nil }
        if root.value(at: "prerelease")?.boolValue == true { return nil }

        var assets: [ReleaseAsset] = []
        if case .array(let entries)? = root.value(at: "assets") {
            for entry in entries {
                guard let name = entry.value(at: "name")?.string, !name.isEmpty,
                      let href = entry.value(at: "browser_download_url")?.string,
                      let url = trustedAssetURL(href, tag: tag, name: name) else { continue }
                assets.append(ReleaseAsset(name: name, url: url))
            }
        }

        let page = root.value(at: "html_url")?.string.flatMap { trustedReleasePage($0, tag: tag) }
        return Release(version: version, assets: assets, page: page)
    }

    private static func trustedAssetURL(_ value: String, tag: String, name: String) -> URL? {
        guard let components = URLComponents(string: value), trustedGitHub(components) else { return nil }
        let path = components.path.split(separator: "/").map(String.init)
        guard path == ["dngkec", "aiusagemeter", "releases", "download", tag, name] else { return nil }
        return components.url
    }

    private static func trustedReleasePage(_ value: String, tag: String) -> URL? {
        guard let components = URLComponents(string: value), trustedGitHub(components) else { return nil }
        let path = components.path.split(separator: "/").map(String.init)
        guard path == ["dngkec", "aiusagemeter", "releases", "tag", tag] else { return nil }
        return components.url
    }

    private static func trustedGitHub(_ components: URLComponents) -> Bool {
        components.scheme?.lowercased() == "https"
            && components.host?.lowercased() == "github.com"
            && components.port == nil
            && components.user == nil
            && components.password == nil
            && components.query == nil
            && components.fragment == nil
    }
}

public enum UpdateCheck {
    /// Decides whether `release` is worth installing over `installed`, and finds the two assets the
    /// install needs. Returns nil when the app is current, when the release carries no package for
    /// this platform, or when the package has no checksum file — an unverifiable download is not
    /// offered at all.
    public static func evaluate(installed: ReleaseVersion, release: Release?, target: UpdateTarget) -> UpdatePackage? {
        guard let release, release.version > installed else { return nil }
        guard let installer = find(installerName(release.version, target), in: release.assets),
              let checksums = find(checksumName(target), in: release.assets) else { return nil }
        return UpdatePackage(version: release.version, installer: installer, checksums: checksums, page: release.page)
    }

    public static func installerName(_ version: ReleaseVersion, _ target: UpdateTarget) -> String {
        switch target {
        case .macOS: return "AIUsageMeter-\(version).dmg"
        case .windowsX64: return "AIUsageMeter-\(version)-win-x64-setup.exe"
        case .windowsArm64: return "AIUsageMeter-\(version)-win-arm64-setup.exe"
        }
    }

    public static func checksumName(_ target: UpdateTarget) -> String {
        switch target {
        case .macOS: return "SHA256SUMS-macos.txt"
        case .windowsX64: return "SHA256SUMS-windows-win-x64.txt"
        case .windowsArm64: return "SHA256SUMS-windows-win-arm64.txt"
        }
    }

    private static func find(_ name: String, in assets: [ReleaseAsset]) -> ReleaseAsset? {
        assets.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }
    }
}

public enum ChecksumFile {
    /// Pulls one file's digest out of a `shasum`-style listing: `<hex>  <name>`, one per line, with
    /// the binary marker `*` allowed before the name. Returns lowercase hex, or nil when the file
    /// does not vouch for `fileName`.
    public static func digest(for fileName: String, in text: String) -> String? {
        for line in text.split(separator: "\n", omittingEmptySubsequences: true) {
            let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !trimmed.isEmpty, !trimmed.hasPrefix("#") else { continue }
            guard let split = trimmed.firstIndex(where: { $0 == " " || $0 == "\t" }) else { continue }

            let digest = String(trimmed[trimmed.startIndex..<split])
            guard digest.count == 64, digest.allSatisfy(\.isHexDigit) else { continue }

            let name = trimmed[trimmed.index(after: split)...]
                .drop { $0 == " " || $0 == "\t" || $0 == "*" }
            if String(name).caseInsensitiveCompare(fileName) == .orderedSame { return digest.lowercased() }
        }
        return nil
    }
}

/// What the About pane and the menu bar show, and nothing more.
public enum UpdateStage: Sendable { case idle, checking, upToDate, available, downloading, ready, installing, failed }

public struct UpdateState: Equatable, Sendable {
    public var stage: UpdateStage
    public var package: UpdatePackage?
    public var progress: Double
    public var message: String?

    public init(stage: UpdateStage = .idle, package: UpdatePackage? = nil, progress: Double = 0, message: String? = nil) {
        self.stage = stage
        self.package = package
        self.progress = progress
        self.message = message
    }

    public var hasUpdate: Bool {
        package != nil && [.available, .downloading, .ready, .installing].contains(stage)
    }
    public var canInstall: Bool { package != nil && (stage == .available || stage == .failed) }
    public var isBusy: Bool { stage == .checking || stage == .downloading || stage == .installing }

    /// The one line of status the About pane shows under the version.
    public var summary: String {
        switch stage {
        case .checking: return "Checking for updates…"
        case .upToDate: return "AIUsageMeter is up to date."
        case .available: return "Version \(package.map(\.version.description) ?? "") is available."
        case .downloading:
            return "Downloading version \(package.map(\.version.description) ?? "")… \(Int(progress * 100))%"
        case .ready: return "Version \(package.map(\.version.description) ?? "") is ready to install."
        case .installing: return "Installing. AIUsageMeter will restart."
        case .failed: return message ?? "The update could not be installed."
        case .idle: return ""
        }
    }

    /// The menu-bar entry, present only while there is something to install.
    public var menuTitle: String? {
        guard hasUpdate, stage != .installing, let package else { return nil }
        return "Update to \(package.version)…"
    }
}
