import Foundation

/// Every outward-facing link the app can open, in one place so the app, the
/// README, and the packaging scripts cannot drift apart. Nothing here is a
/// provider endpoint: these are only ever opened in the user's browser, on an
/// explicit click, and they carry nothing about the user with them.
public enum SupportLinks {
    /// The public source repository.
    public static let repository = URL(string: "https://github.com/dngkec/usagemeter")!
    /// Where a report or a request should land.
    public static let issues = URL(string: "https://github.com/dngkec/usagemeter/issues")!
    /// Buy Me a Coffee, the one place the project asks for anything.
    public static let sponsor = URL(string: "https://buymeacoffee.com/dngkec")!
    /// The designer the interface came from.
    public static let designer = URL(string: "https://x.com/hivinz_")!

    public static let sponsorLabel = "Buy me a coffee"
    public static let repositoryLabel = "View on GitHub"
    public static let designerHandle = "@hivinz_"
    public static let designerCredit = "Interface design by @hivinz_"

    /// Shown wherever there is room for a sentence rather than a label.
    public static let sponsorBlurb =
        "UsageMeter is free and open source. If it saves you a trip to a billing dashboard, a coffee keeps it going."

    /// Everything the app is willing to open. A link the app offers is opened
    /// through this, so an unexpected URL can never reach the browser by
    /// travelling through a support surface.
    public static let all: [URL] = [repository, issues, sponsor, designer]

    public static func isSupported(_ url: URL) -> Bool { all.contains(url) }
}
