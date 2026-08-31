// swift-tools-version: 6.0
import Foundation
import PackageDescription

/// A Command Line Tools install leaves swift-testing's macro plugin where the
/// compiler does not look; an Xcode toolchain needs none of it and rejects a
/// foreign plugin, so the paths are added only for the former.
private func selectedDeveloperDirectory() -> String {
    let commandLineTools = "/Library/Developer/CommandLineTools"
    let files = FileManager.default
    if let overridden = ProcessInfo.processInfo.environment["DEVELOPER_DIR"], files.fileExists(atPath: overridden) {
        return overridden
    }
    // `xcode-select -p` reads this link.
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

/// SwiftPM refuses to load a manifest declaring a target whose directory is
/// missing, and `Tests/` is not published, so the test target is declared only
/// where it exists. The manifest is cached: adding or removing `Tests/` may need
/// `--manifest-cache none` once before the change is noticed.
private let testsPath = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent()
    .appendingPathComponent("Tests/AIUsageMeterCoreTests")
private let hasTests = FileManager.default.fileExists(atPath: testsPath.path)

private let testTargets: [Target] = hasTests
    ? [.testTarget(
        name: "AIUsageMeterCoreTests",
        dependencies: ["AIUsageMeterCore"],
        resources: [.process("Fixtures")],
        swiftSettings: testSwiftSettings,
        linkerSettings: testLinkerSettings
    )]
    : []

let package = Package(
    name: "AIUsageMeter",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "AIUsageMeterCore", targets: ["AIUsageMeterCore"]),
        .executable(name: "AIUsageMeter", targets: ["AIUsageMeter"]),
    ],
    targets: [
        .target(
            name: "AIUsageMeterCore",
            linkerSettings: [.linkedFramework("Security")]
        ),
        .executableTarget(
            name: "AIUsageMeter",
            dependencies: ["AIUsageMeterCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("SwiftUI"),
                .linkedFramework("ServiceManagement"),
            ]
        ),
    ] + testTargets,
    swiftLanguageModes: [.v5]
)
