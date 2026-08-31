import Foundation

public enum SupportLinks {
    public static let repository = URL(string: "https://github.com/dngkec/aiusagemeter")!
    public static let issues = URL(string: "https://github.com/dngkec/aiusagemeter/issues")!
    public static let sponsor = URL(string: "https://buymeacoffee.com/dngkec")!
    public static let designer = URL(string: "https://x.com/hivinz_")!

    public static let sponsorLabel = "Buy me a coffee"
    public static let repositoryLabel = "View on GitHub"
    public static let designerHandle = "@hivinz_"
    public static let designerCredit = "Design inspired by @hivinz_"

    public static let sponsorBlurb =
        "AIUsageMeter is free and open source. A coffee keeps it going."

    public static let all: [URL] = [repository, issues, sponsor, designer]

    public static func isSupported(_ url: URL) -> Bool { all.contains(url) }
}
