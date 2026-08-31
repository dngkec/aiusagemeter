import Foundation

public protocol HTTPClient: Sendable {
    func data(for request: URLRequest, maximumBytes: Int) async throws -> (Data, HTTPURLResponse)
}

/// The body arrives in chunks from the session delegate rather than through `URLSession.bytes`:
/// a per-byte read costs roughly 8 µs a byte — 125 ms for a 16 KB reading — and every enabled
/// provider pays it on every refresh, which is what made a long provider list feel slow.
public final class BoundedHTTPClient: HTTPClient, @unchecked Sendable {
    private let session: URLSession
    private let sink = BoundedResponseSink()

    public static var defaultConfiguration: URLSessionConfiguration {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 15
        config.timeoutIntervalForResource = 25
        config.waitsForConnectivity = false
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        config.httpMaximumConnectionsPerHost = 3
        return config
    }

    public init(configuration: URLSessionConfiguration = BoundedHTTPClient.defaultConfiguration) {
        self.session = URLSession(configuration: configuration, delegate: sink, delegateQueue: nil)
    }

    deinit { session.finishTasksAndInvalidate() }

    public func data(for request: URLRequest, maximumBytes: Int = 2_000_000) async throws -> (Data, HTTPURLResponse) {
        let task = session.dataTask(with: request)
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<(Data, HTTPURLResponse), any Error>) in
                sink.begin(task, limit: maximumBytes, continuation: continuation)
                task.resume()
            }
        } onCancel: {
            task.cancel()
        }
    }

    static func check(_ response: HTTPURLResponse) throws {
        switch response.statusCode {
        case 200..<300: return
        case 401, 403: throw AIUsageMeterError.unauthorized
        case 429: throw AIUsageMeterError.rateLimited
        default: throw AIUsageMeterError.server(response.statusCode)
        }
    }

    static func translate(_ error: any Error) -> any Error {
        guard let urlError = error as? URLError else { return error }
        switch urlError.code {
        case .timedOut: return AIUsageMeterError.timeout
        case .notConnectedToInternet, .networkConnectionLost, .cannotFindHost, .cannotConnectToHost, .dnsLookupFailed:
            return AIUsageMeterError.offline
        default: return error
        }
    }
}

/// One transfer's state. A class, so appending a chunk mutates the buffer in place instead of
/// copying it out of a dictionary and back on every callback.
private final class BoundedTransfer {
    let limit: Int
    let continuation: CheckedContinuation<(Data, HTTPURLResponse), any Error>
    var body = Data()
    var oversized = false

    init(limit: Int, continuation: CheckedContinuation<(Data, HTTPURLResponse), any Error>) {
        self.limit = limit
        self.continuation = continuation
    }
}

/// Enforces the size cap as the transfer runs: an announced length over the cap is refused before the
/// body is read, and an unannounced one is cut off at the chunk that would cross it.
private final class BoundedResponseSink: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    private let lock = NSLock()
    private var transfers: [Int: BoundedTransfer] = [:]

    func begin(_ task: URLSessionTask, limit: Int, continuation: CheckedContinuation<(Data, HTTPURLResponse), any Error>) {
        lock.withLock { transfers[task.taskIdentifier] = BoundedTransfer(limit: limit, continuation: continuation) }
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        let refuse = lock.withLock { () -> Bool in
            guard let transfer = transfers[dataTask.taskIdentifier],
                  response.expectedContentLength > Int64(transfer.limit) else { return false }
            transfer.oversized = true
            return true
        }
        completionHandler(refuse ? .cancel : .allow)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        let refuse = lock.withLock { () -> Bool in
            guard let transfer = transfers[dataTask.taskIdentifier], !transfer.oversized else { return false }
            guard transfer.body.count + data.count <= transfer.limit else {
                transfer.oversized = true
                transfer.body = Data()
                return true
            }
            transfer.body.append(data)
            return false
        }
        if refuse { dataTask.cancel() }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: (any Error)?) {
        guard let transfer = lock.withLock({ transfers.removeValue(forKey: task.taskIdentifier) }) else { return }
        if transfer.oversized {
            transfer.continuation.resume(throwing: AIUsageMeterError.oversizedResponse)
        } else if let error {
            transfer.continuation.resume(throwing: BoundedHTTPClient.translate(error))
        } else if let http = task.response as? HTTPURLResponse {
            do {
                try BoundedHTTPClient.check(http)
                transfer.continuation.resume(returning: (transfer.body, http))
            } catch {
                transfer.continuation.resume(throwing: error)
            }
        } else {
            transfer.continuation.resume(throwing: AIUsageMeterError.invalidResponse)
        }
    }
}
public enum EndpointValidator {
    public static func validate(_ string: String) throws -> URL {
        guard let components = URLComponents(string: string), let scheme = components.scheme?.lowercased(), let host = components.host?.lowercased(), !host.isEmpty, components.user == nil, components.password == nil else {
            throw AIUsageMeterError.invalidURL("Enter a complete URL without embedded credentials.")
        }
        if scheme == "https" { return components.url! }
        let localHosts = ["localhost", "127.0.0.1", "::1"]
        if scheme == "http", localHosts.contains(host) { return components.url! }
        throw AIUsageMeterError.invalidURL("HTTPS is required; HTTP is allowed only for localhost.")
    }
}

public enum RequestFactory {
    public static let defaultUserAgent = "AIUsageMeter/1.0"

    /// The usage endpoint buckets rate limits by client; an unnamed caller is throttled into 429s.
    public static let claudeCodeUserAgent = "claude-code/2.0.0 (AIUsageMeter/1.0)"

    public static func request(url: URL, method: HTTPMethod = .get, bearer: String? = nil, headers: [String: String] = [:], userAgent: String = defaultUserAgent) -> URLRequest {
        var request = URLRequest(url: url, timeoutInterval: 15)
        request.httpMethod = method.rawValue
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        if method == .post {
            request.httpBody = Data("{}".utf8)
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        if let bearer, !bearer.isEmpty { request.setValue("Bearer \(bearer)", forHTTPHeaderField: "Authorization") }
        for (key, value) in headers where !key.isEmpty { request.setValue(value, forHTTPHeaderField: key) }
        return request
    }
}

public final class StubHTTPClient: HTTPClient, @unchecked Sendable {
    public typealias Handler = @Sendable (URLRequest) async throws -> (Data, HTTPURLResponse)
    private let handler: Handler
    public init(handler: @escaping Handler) { self.handler = handler }
    public func data(for request: URLRequest, maximumBytes: Int) async throws -> (Data, HTTPURLResponse) {
        let result = try await handler(request)
        guard result.0.count <= maximumBytes else { throw AIUsageMeterError.oversizedResponse }
        return result
    }
}
