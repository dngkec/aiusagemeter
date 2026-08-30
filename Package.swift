// swift-tools-version: 6.0
import Foundation
import PackageDescription

/// swift-testing ships with the toolchain, but a Command Line Tools install
/// leaves its macro plugin and its frameworks somewhere the compiler does not
/// look on its own. Point at them when that is the toolchain in use, and add
/// nothing at all when it is not — an Xcode toolchain, which is what CI and
/// most contributors have, needs none of it and rejects a foreign plugin.
private func selectedDeveloperDirectory() -> String {
    let commandLineTools = "/Library/Developer/CommandLineTools"
    let files = FileManager.default
    if let overridden = ProcessInfo.processInfo.environment["DEVELOPER_DIR"], files.fileExists(atPath: overridden) {
        return overridden
    }
    // `xcode-select -p` reads this link, and falls back to Command Line Tools
    // when what it points at is not actually installed.
    if let linked = try? files.destinationOfSymbolicLink(atPath: "/var/db/xcode_select_link"), files.fileExists(atPath: linked) {
        return linked
    }
    return commandLineTools
}

private let developerDirectory = selectedDeveloperDirectory()
private let testingPlugin = "\(developerDirectory)/usr/lib/swift/host/plugins/testing/libTestingMacros.dylib"
private let needsTestingPaths = developerDirectory.hasSuffix("CommandLineTools")
    && FileManager.default.fileExists(atPath: testingPlugin)

private let testSwiftSettings: [SwiftSetting] = needsTestingPaths
    ? [.unsafeFlags(["-load-plugin-library", testingPlugin])]
    : []

private let testLinkerSettings: [LinkerSetting] = needsTestingPaths
    ? [.unsafeFlags([
        "-Xlinker", "-rpath", "-Xlinker", "\(developerDirectory)/Library/Developer/Frameworks",
        "-Xlinker", "-rpath", "-Xlinker", "\(developerDirectory)/Library/Developer/usr/lib",
    ])]
    : []

/// The test suite is not part of the published repository, and SwiftPM refuses
/// to load a manifest that declares a target whose directory is missing. So the
/// test target is declared only where it exists: `swift test` works in a working
/// copy that has `Tests/`, and a fresh clone still builds and packages the app.
///
/// SwiftPM caches the evaluated manifest, so adding or removing `Tests/` in an
/// existing working copy may need `--manifest-cache none` once, or any edit to
/// this file, before the change is noticed.
private let testsPath = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent()
    .appendingPathComponent("Tests/UsageMeterCoreTests")
private let hasTests = FileManager.default.fileExists(atPath: testsPath.path)

private let testTargets: [Target] = hasTests
    ? [.testTarget(
        name: "UsageMeterCoreTests",
        dependencies: ["UsageMeterCore"],
        resources: [.process("Fixtures")],
        swiftSettings: testSwiftSettings,
        linkerSettings: testLinkerSettings
    )]
    : []

let package = Package(
    name: "UsageMeter",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "UsageMeterCore", targets: ["UsageMeterCore"]),
        .executable(name: "UsageMeter", targets: ["UsageMeter"]),
    ],
    targets: [
        .target(
            name: "UsageMeterCore",
            linkerSettings: [.linkedFramework("Security")]
        ),
        .executableTarget(
            name: "UsageMeter",
            dependencies: ["UsageMeterCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("SwiftUI"),
                .linkedFramework("ServiceManagement"),
            ]
        ),
    ] + testTargets,
    swiftLanguageModes: [.v5]
)
