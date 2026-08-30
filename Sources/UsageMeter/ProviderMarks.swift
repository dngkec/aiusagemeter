import AppKit
import UsageMeterCore

/// Official artwork, when the app owner has supplied it.
///
/// UsageMeter draws its own marks so that it never redistributes vendor logos.
/// Dropping a file into `Resources/ProviderMarks` overrides the drawn mark for
/// that provider; see the README in that folder.
enum ProviderMarks {
    struct Mark {
        let image: NSImage
        /// Tint to match the rail, rather than using the file's own colours.
        let template: Bool
    }

    private static let extensions = ["pdf", "png", "tiff", "jpg", "jpeg"]

    private static let marks: [ProviderID: Mark] = {
        guard let root = Bundle.main.resourceURL?.appendingPathComponent("ProviderMarks", isDirectory: true),
              FileManager.default.fileExists(atPath: root.path) else { return [:] }
        var found: [ProviderID: Mark] = [:]
        for id in ProviderID.allCases {
            for suffix in extensions {
                if let image = NSImage(contentsOf: root.appendingPathComponent("\(id.rawValue).color.\(suffix)")) {
                    found[id] = Mark(image: image, template: false)
                    break
                }
                if let image = NSImage(contentsOf: root.appendingPathComponent("\(id.rawValue).\(suffix)")) {
                    found[id] = Mark(image: image, template: true)
                    break
                }
            }
        }
        return found
    }()

    static func mark(for id: ProviderID) -> Mark? { marks[id] }
}
