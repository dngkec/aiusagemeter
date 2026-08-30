import Foundation

public protocol HTTPClient: Sendable {
    func data(for request: URLRequest, maximumBytes: Int) async throws -> (Data, HTTPURLResponse)
}

public final class BoundedHTTPClient: NSObject, HTTPClient, URLSessionDataDelegate, @unchecked Sendable {
    private let session: URLSession
    public override init() {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 15
        config.timeoutIntervalForResource = 25
        config.waitsForConnectivity = false
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        config.httpMaximumConnectionsPerHost = 3
        self.session = URLSession(configuration: config)
        super.init()
    }
    public init(session: URLSession) { self.session = session; super.init() }

    public func data(for request: URLRequest, maximumBytes: Int = 2_000_000) async throws -> (Data, HTTPURLResponse) {
        do {
            let (bytes, response) = try await session.bytes(for: request)
            guard let http = response as? HTTPURLResponse else { throw UsageMeterError.invalidResponse }
            switch http.statusCode {
            case 200..<300: break
            case 401, 403: throw UsageMeterError.unauthorized
            case 429: throw UsageMeterError.rateLimited
            default: throw UsageMeterError.server(http.statusCode)
            }
            var data = Data(); data.reserveCapacity(min(maximumBytes, 64 * 1024))
            for try await byte in bytes {
                guard data.count < maximumBytes else { throw UsageMeterError.oversizedResponse }
                data.append(byte)
            }
            return (data, http)
        } catch let error as UsageMeterError { throw error }
        catch let error as URLError {
            switch error.code {
            case .timedOut: throw UsageMeterError.timeout
            case .notConnectedToInternet, .networkConnectionLost, .cannotFindHost, .cannotConnectToHost, .dnsLookupFailed: throw UsageMeterError.offline
            default: throw error
            }
        }
    }
}

public enum EndpointValidator {
    public static func validate(_ string: String) throws -> URL {
        guard let components = URLComponents(string: string), let scheme = components.scheme?.lowercased(), let host = components.host?.lowercased(), !host.isEmpty, components.user == nil, components.password == nil else {
            throw UsageMeterError.invalidURL("Enter a complete URL without embedded credentials.")
        }
        if scheme == "https" { return components.url! }
        let localHosts = ["localhost", "127.0.0.1", "::1"]
        if scheme == "http", localHosts.contains(host) { return components.url! }
        throw UsageMeterError.invalidURL("HTTPS is required; HTTP is allowed only for localhost.")
    }
}

public enum RequestFactory {
    public static let defaultUserAgent = "UsageMeter/1.0"

    /// Claude Code's usage endpoint buckets rate limits by client. A request
    /// that does not identify itself as Claude Code lands in a far stricter
    /// bucket and answers 429 almost immediately, so UsageMeter names both.
    public static let claudeCodeUserAgent = "claude-code/2.0.0 (UsageMeter/1.0)"

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
        guard result.0.count <= maximumBytes else { throw UsageMeterError.oversizedResponse }
        return result
    }
}
