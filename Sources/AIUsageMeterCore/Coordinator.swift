import Foundation

public struct ProviderRegistry: Sendable {
    private let adapters: [ProviderID: any ProviderAdapter]
    public init(adapters: [any ProviderAdapter] = ProviderID.allCases.map(GenericProviderAdapter.init)) {
        self.adapters = Dictionary(uniqueKeysWithValues: adapters.map { ($0.id, $0) })
    }
    public func adapter(for id: ProviderID) -> (any ProviderAdapter)? { adapters[id] }
}

public actor RefreshCoordinator {
    private let registry: ProviderRegistry
    private let context: ProviderContext
    private var activeTask: Task<[ProviderSnapshot], Never>?
    private var refreshID: UUID?

    public init(registry: ProviderRegistry = .init(), context: ProviderContext) {
        self.registry = registry; self.context = context
    }

    public func cancel() { activeTask?.cancel(); activeTask = nil; refreshID = nil }

    public func refresh(preferences: AppPreferences) async -> [ProviderSnapshot] {
        activeTask?.cancel()
        let currentID = UUID()
        refreshID = currentID
        let registry = self.registry, context = self.context
        let configurations = preferences.providers.filter(\.enabled)
        let demo = preferences.demoData
        let task = Task<[ProviderSnapshot], Never> {
            if demo { return DemoData.snapshots(for: configurations.map(\.id)) }
            return await withTaskGroup(of: (Int, ProviderSnapshot).self, returning: [ProviderSnapshot].self) { group in
                for (index, configuration) in configurations.enumerated() {
                    group.addTask {
                        guard !Task.isCancelled else { return (index, ProviderSnapshot(id: configuration.id, status: .error, message: "Refresh cancelled")) }
                        guard let adapter = registry.adapter(for: configuration.id) else { return (index, ProviderSnapshot(id: configuration.id, status: .setupNeeded, message: "No adapter is registered.")) }
                        do { return (index, try await adapter.fetch(configuration: configuration, context: context)) }
                        catch { return (index, Self.failure(id: configuration.id, error: error)) }
                    }
                }
                var output: [(Int, ProviderSnapshot)] = []
                for await item in group { output.append(item) }
                return output.sorted { $0.0 < $1.0 }.map(\.1)
            }
        }
        activeTask = task
        let result = await task.value
        if refreshID == currentID { activeTask = nil; refreshID = nil }
        return result
    }

    nonisolated static func failure(id: ProviderID, error: Error) -> ProviderSnapshot {
        let status: ProviderStatus
        switch error {
        case AIUsageMeterError.setupNeeded: status = .setupNeeded
        case AIUsageMeterError.unauthorized: status = .unauthorized
        case AIUsageMeterError.rateLimited: status = .rateLimited
        case AIUsageMeterError.offline, URLError.notConnectedToInternet: status = .offline
        case AIUsageMeterError.expiredCredential: status = .expired
        default: status = .error
        }
        return ProviderSnapshot(id: id, status: status, message: (error as? LocalizedError)?.errorDescription ?? "Refresh failed")
    }
}

public enum DemoData {
    public static func snapshots(for ids: [ProviderID], now: Date = Date(timeIntervalSince1970: 1_800_000_000)) -> [ProviderSnapshot] {
        let percents: [Double] = [73, 21, 52, 8, 64, 91, 37, 46]
        let selected = ids.isEmpty ? [.claude, .codex, .gemini] : ids
        return selected.enumerated().map { index, id in
            let first = percents[index % percents.count]
            let second = percents[(index + 4) % percents.count] / 3
            return ProviderSnapshot(
                id: id,
                windows: [
                    UsageWindow(id: "session", label: "Current session", used: first, limit: 100, resetsAt: now.addingTimeInterval(51 * 60)),
                    UsageWindow(id: "all", label: "All models", used: second, limit: 100, resetsAt: now.addingTimeInterval(13 * 3600)),
                ],
                source: .demo,
                message: "Deterministic visual test data",
                updatedAt: now
            )
        }
    }
}
