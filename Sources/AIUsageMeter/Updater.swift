import AppKit
import CryptoKit
import Foundation
import AIUsageMeterCore

/// Checks GitHub for a newer release and, when the user asks for it, installs one.
///
/// The disk image is verified against the `SHA256SUMS-macos.txt` published beside it before it is
/// mounted, and the bundle is replaced by a short script that waits for this process to exit —
/// an app cannot overwrite itself while it is running. Nothing downloads until the user asks.
@MainActor
final class Updater {
    /// Long enough that a machine woken from sleep is not asked to check twice in a day.
    private static let interval: Duration = .seconds(24 * 60 * 60)
    /// The first check waits for the opening refresh to finish rather than racing it.
    private static let firstDelay: Duration = .seconds(10)
    /// A disk image is around 20 MB; this only stops an unbounded write.
    private static let maximumImageBytes = 400 * 1024 * 1024
    private static let maximumChecksumBytes = 64 * 1024

    private let session: URLSession
    private var schedule: Task<Void, Never>?
    private var work: Task<Void, Never>?
    private var checking = false

    private(set) var state = UpdateState() {
        didSet { if state != oldValue { onChange?() } }
    }
    var onChange: (() -> Void)?

    init() {
        // Its own session rather than the shared BoundedHTTPClient: that one caps a response at
        // 2 MB, and a disk image is far larger than any provider reading.
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 30
        configuration.timeoutIntervalForResource = 600
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        session = URLSession(configuration: configuration)
    }

    /// The running bundle's version. Read from Info.plist rather than a constant so it can only
    /// ever be the version that was actually built.
    static var currentVersion: ReleaseVersion {
        ReleaseVersion(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? ReleaseVersion(0, 0, 0)
    }

    func start() {
        schedule?.cancel()
        schedule = Task { [weak self] in
            try? await Task.sleep(for: Updater.firstDelay)
            while !Task.isCancelled {
                await self?.check(quiet: true)
                try? await Task.sleep(for: Updater.interval)
            }
        }
    }

    func stop() {
        schedule?.cancel()
        work?.cancel()
    }

    /// Asks GitHub what the newest release is. `quiet` is the scheduled check: it leaves the pane
    /// alone rather than reporting that a background poll found no network.
    func check(quiet: Bool = false) async {
        guard !checking, work == nil, !state.isBusy else { return }
        checking = true
        defer { checking = false }
        if !quiet { state = UpdateState(stage: .checking) }
        do {
            let data = try await boundedData(for: ReleaseFeed.request(), maximumBytes: ReleaseFeed.maximumBytes)

            let package = UpdateCheck.evaluate(installed: Updater.currentVersion, release: ReleaseFeed.parse(data), target: .macOS)
            state = package.map { UpdateState(stage: .available, package: $0) } ?? UpdateState(stage: .upToDate)
        } catch is CancellationError {
        } catch {
            // A failed check is not a failed update: nothing was promised, so say nothing on the
            // scheduled poll and keep whatever the pane already showed.
            if !quiet {
                state = UpdateState(stage: .failed, message: "Could not reach GitHub to check for updates.")
            }
        }
    }

    /// Downloads the offered image, verifies it, and hands the swap to a detached script.
    func install() {
        guard !checking, work == nil, let package = state.package, !state.isBusy else { return }
        work?.cancel()
        state = UpdateState(stage: .downloading, package: package)
        work = Task { [weak self] in
            guard let self else { return }
            defer { self.work = nil }
            do {
                let digest = try await self.digest(for: package)
                let image = try await self.download(package, expecting: digest)
                self.state = UpdateState(stage: .ready, package: package, progress: 1)
                try self.swap(using: image, package: package)
            } catch is CancellationError {
            } catch let error as UpdateFailure {
                self.state = UpdateState(stage: .failed, package: package, message: error.message)
            } catch {
                self.state = UpdateState(stage: .failed, package: package,
                                         message: "The update could not be downloaded. Try again later.")
            }
        }
    }

    /// The last percentage posted, so a download reports once per percent rather than per callback.
    private var shownPercent = -1

    /// Called from the download delegate, one hop later.
    private func report(_ fraction: Double, for package: UpdatePackage) {
        let percent = Int(fraction * 100)
        guard percent != shownPercent, state.stage == .downloading else { return }
        shownPercent = percent
        state = UpdateState(stage: .downloading, package: package, progress: fraction)
    }

    // MARK: - Download

    /// Reads the digest the release publishes for the image we are about to fetch.
    private func digest(for package: UpdatePackage) async throws -> String {
        let request = URLRequest(url: package.checksums.url)
        guard let text = String(data: try await boundedData(for: request, maximumBytes: Updater.maximumChecksumBytes),
                                encoding: .utf8) else {
            throw UpdateFailure("The release does not publish a checksum for this build.")
        }
        guard let digest = ChecksumFile.digest(for: package.installer.name, in: text) else {
            throw UpdateFailure("The release does not publish a checksum for this build.")
        }
        return digest
    }

    /// Downloads the image to disk and refuses to keep a file whose digest does not match. Nothing
    /// is mounted before this returns.
    ///
    /// `URLSession.download` rather than `bytes`: an `AsyncBytes` loop costs roughly 8 µs a byte,
    /// which is a quarter of an hour for a 20 MB image. Progress arrives through the delegate.
    private func download(_ package: UpdatePackage, expecting digest: String) async throws -> URL {
        let directory = try updatesDirectory()
        sweep(directory, keeping: package.installer.name)
        let destination = directory.appendingPathComponent(package.installer.name)

        // An interrupted attempt may have left a good copy behind; hashing it beats fetching again.
        if FileManager.default.fileExists(atPath: destination.path), try matches(destination, digest) {
            return destination
        }

        shownPercent = -1
        let progress = DownloadProgress(maximumBytes: Int64(Updater.maximumImageBytes)) { [weak self] fraction in
            Task { @MainActor in self?.report(fraction, for: package) }
        }

        let (temporary, response) = try await session.download(from: package.installer.url, delegate: progress)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            try? FileManager.default.removeItem(at: temporary)
            throw UpdateFailure("The update could not be downloaded. Try again later.")
        }

        let attributes = try? FileManager.default.attributesOfItem(atPath: temporary.path)
        let size = (attributes?[.size] as? Int) ?? 0
        guard size > 0, size <= Updater.maximumImageBytes else {
            try? FileManager.default.removeItem(at: temporary)
            throw UpdateFailure("The published disk image is not the size it claims to be.")
        }

        guard try matches(temporary, digest) else {
            // Left on disk it would be an unverified image in a directory we later mount from.
            try? FileManager.default.removeItem(at: temporary)
            throw UpdateFailure("The download did not match the checksum GitHub published. Nothing was installed.")
        }

        try? FileManager.default.removeItem(at: destination)
        try FileManager.default.moveItem(at: temporary, to: destination)
        return destination
    }

    // MARK: - Install

    /// Copies the verified bundle beside the installed one, validates it, then uses Foundation's
    /// same-volume replacement so a failed copy can never delete the working app.
    private func swap(using image: URL, package: UpdatePackage) throws {
        let bundle = Bundle.main.bundleURL
        guard FileManager.default.isWritableFile(atPath: bundle.deletingLastPathComponent().path),
              !bundle.path.hasPrefix("/Volumes/") else {
            // Installed somewhere this user may not write, or still running from the image itself.
            // The download is verified either way, so hand it to Finder rather than fail outright.
            NSWorkspace.shared.activateFileViewerSelecting([image])
            throw UpdateFailure("AIUsageMeter cannot replace itself where it is installed. "
                                + "The verified download is now shown in Finder — drag it into Applications.")
        }

        let mount = try attach(image)
        var mounted = true
        defer { if mounted { detach(mount) } }
        let source = mount.appendingPathComponent("AIUsageMeter.app")
        try validate(source, version: package.version)

        let parent = bundle.deletingLastPathComponent()
        let staged = parent.appendingPathComponent(".AIUsageMeter-update-\(UUID().uuidString).app")
        let backup = parent.appendingPathComponent(".AIUsageMeter-backup-\(UUID().uuidString).app")
        defer { try? FileManager.default.removeItem(at: staged) }
        do {
            _ = try run("/usr/bin/ditto", [source.path, staged.path],
                        failure: "The new app could not be staged beside the installed copy.")
            try validate(staged, version: package.version)
        } catch {
            throw error as? UpdateFailure ?? UpdateFailure("The new app could not be staged beside the installed copy.")
        }

        detach(mount)
        mounted = false

        let script = try writeRelaunchScript(for: bundle, backup: backup)
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        process.arguments = [script.path, String(ProcessInfo.processInfo.processIdentifier)]
        do { try process.run() } catch {
            try? FileManager.default.removeItem(at: script)
            throw UpdateFailure("The update could not be started.")
        }

        do {
            _ = try FileManager.default.replaceItemAt(
                bundle,
                withItemAt: staged,
                backupItemName: backup.lastPathComponent,
                options: [.withoutDeletingBackupItem]
            )
        } catch {
            process.terminate()
            try? FileManager.default.removeItem(at: script)
            if !FileManager.default.fileExists(atPath: bundle.path),
               FileManager.default.fileExists(atPath: backup.path) {
                try? FileManager.default.moveItem(at: backup, to: bundle)
            }
            throw UpdateFailure("The installed app could not be replaced. The existing copy or its backup was kept.")
        }

        state = UpdateState(stage: .installing, package: package, progress: 1)
        // The replacement is complete; the helper waits for this process to leave before reopening it.
        NSApp.terminate(nil)
    }

    private func validate(_ app: URL, version: ReleaseVersion) throws {
        let values = try? app.resourceValues(forKeys: [.isDirectoryKey])
        guard let expectedIdentifier = Bundle.main.bundleIdentifier, !expectedIdentifier.isEmpty,
              values?.isDirectory == true,
              let candidate = Bundle(url: app),
              candidate.bundleIdentifier == expectedIdentifier,
              candidate.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == version.description else {
            throw UpdateFailure("The disk image did not contain the expected AIUsageMeter version.")
        }
        _ = try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path],
                    failure: "The app in the disk image has an invalid code signature.")
    }

    /// Mounts read-only and without Finder, and returns the mount point `hdiutil` reports.
    private func attach(_ image: URL) throws -> URL {
        let output = try run("/usr/bin/hdiutil",
                             ["attach", image.path, "-nobrowse", "-readonly", "-noautoopen", "-plist"],
                             failure: "The disk image could not be opened.")
        guard let plist = (try? PropertyListSerialization.propertyList(from: Data(output.utf8), format: nil)) as? [String: Any],
              let entities = plist["system-entities"] as? [[String: Any]],
              let point = entities.compactMap({ $0["mount-point"] as? String }).first(where: { !$0.isEmpty }) else {
            throw UpdateFailure("The disk image could not be opened.")
        }
        return URL(fileURLWithPath: point)
    }

    private func detach(_ mount: URL) {
        _ = try? run("/usr/bin/hdiutil", ["detach", mount.path, "-quiet"], failure: "")
    }

    @discardableResult
    private func run(_ tool: String, _ arguments: [String], failure: String) throws -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        try process.run()
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw UpdateFailure(failure) }
        return String(data: data, encoding: .utf8) ?? ""
    }

    private func writeRelaunchScript(for bundle: URL, backup: URL) throws -> URL {
        let script = """
        #!/bin/sh
        PID="$1"
        for _ in $(seq 1 300); do
          kill -0 "$PID" 2>/dev/null || break
          sleep 0.1
        done
        if kill -0 "$PID" 2>/dev/null; then
          if [ -d \(shellQuoted(backup.path)) ]; then
            /bin/rm -rf \(shellQuoted(bundle.path))
            /bin/mv \(shellQuoted(backup.path)) \(shellQuoted(bundle.path))
          fi
          rm -f "$0"
          exit 1
        fi
        LAUNCHED=0
        if /usr/bin/open \(shellQuoted(bundle.path)); then
          for _ in $(seq 1 50); do
            if /usr/bin/pgrep -x AIUsageMeter >/dev/null 2>&1; then
              sleep 2
              /usr/bin/pgrep -x AIUsageMeter >/dev/null 2>&1 && LAUNCHED=1
              break
            fi
            sleep 0.1
          done
        fi
        if [ "$LAUNCHED" -eq 1 ]; then
          /bin/rm -rf \(shellQuoted(backup.path))
        else
          /bin/rm -rf \(shellQuoted(bundle.path))
          /bin/mv \(shellQuoted(backup.path)) \(shellQuoted(bundle.path))
          /usr/bin/open \(shellQuoted(bundle.path)) || true
        fi
        rm -f "$0"

        """
        let path = try updatesDirectory().appendingPathComponent("install.sh")
        try script.write(to: path, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: path.path)
        return path
    }

    private func shellQuoted(_ value: String) -> String {
        "'" + value.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    // MARK: - Files

    private func updatesDirectory() throws -> URL {
        let caches = try FileManager.default.url(for: .cachesDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        let directory = caches
            .appendingPathComponent(Bundle.main.bundleIdentifier ?? "app.aiusagemeter.AIUsageMeter")
            .appendingPathComponent("updates")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }

    /// Clears images left by earlier updates, so the folder holds one file at most.
    private func sweep(_ directory: URL, keeping name: String) {
        let contents = (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? []
        for file in contents where file.lastPathComponent != name {
            try? FileManager.default.removeItem(at: file)
        }
    }

    private func matches(_ path: URL, _ digest: String) throws -> Bool {
        guard let handle = try? FileHandle(forReadingFrom: path) else { return false }
        defer { try? handle.close() }
        var hash = SHA256()
        while let chunk = try handle.read(upToCount: 1024 * 1024), !chunk.isEmpty {
            hash.update(data: chunk)
        }
        return hash.finalize().hexadecimal == digest
    }

    private func boundedData(for request: URLRequest, maximumBytes: Int) async throws -> Data {
        let (temporary, response) = try await session.download(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw AIUsageMeterError.invalidResponse
        }
        if let length = http.value(forHTTPHeaderField: "Content-Length").flatMap(Int.init), length > maximumBytes {
            throw AIUsageMeterError.oversizedResponse
        }
        let size = (try? temporary.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? maximumBytes + 1
        guard size <= maximumBytes else { throw AIUsageMeterError.oversizedResponse }
        return try Data(contentsOf: temporary)
    }
}

/// A failure with a sentence the About pane can show as it is.
private struct UpdateFailure: Error {
    let message: String
    init(_ message: String) { self.message = message }
}

private extension SHA256Digest {
    var hexadecimal: String { map { String(format: "%02x", $0) }.joined() }
}

/// Reports how much of a download has arrived. `URLSession`'s async `download` never calls
/// `didFinishDownloadingTo` — it returns the file itself — but the protocol still requires it.
private final class DownloadProgress: NSObject, URLSessionDownloadDelegate, @unchecked Sendable {
    private let maximumBytes: Int64
    private let report: @Sendable (Double) -> Void

    init(maximumBytes: Int64, _ report: @escaping @Sendable (Double) -> Void) {
        self.maximumBytes = maximumBytes
        self.report = report
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask,
                    didWriteData bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        if totalBytesWritten > maximumBytes || totalBytesExpectedToWrite > maximumBytes {
            downloadTask.cancel()
            return
        }
        guard totalBytesExpectedToWrite > 0 else { return }
        report(min(1, Double(totalBytesWritten) / Double(totalBytesExpectedToWrite)))
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {}
}
