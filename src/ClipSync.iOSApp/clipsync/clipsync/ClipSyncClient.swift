import Foundation

enum ClipSyncError: Error {
    case missingConfiguration
    case invalidResponse
}

struct ClipboardPayload: Decodable {
    let content: String
    let updatedAt: String
}

/// Talks to the same REST API the Windows tray app hosts and the Shortcuts
/// client polls — this app's only extra step is registering for push wake-ups.
final class ClipSyncClient {
    static let shared = ClipSyncClient()

    private var baseURL: URL? {
        guard let raw = UserDefaults.standard.string(forKey: "serverBaseURL"),
              let url = URL(string: raw) else { return nil }
        return url
    }

    private var pairingKey: String {
        UserDefaults.standard.string(forKey: "pairingKey") ?? ""
    }

    private func makeRequest(path: String, method: String) throws -> URLRequest {
        guard let base = baseURL else { throw ClipSyncError.missingConfiguration }
        var request = URLRequest(url: base.appendingPathComponent(path))
        request.httpMethod = method
        request.setValue(pairingKey, forHTTPHeaderField: "X-ClipSync-Key")
        return request
    }

    func fetchLatestClipboard() async throws -> String {
        let request = try makeRequest(path: "clipboard/latest", method: "GET")
        let (data, response) = try await URLSession.shared.data(for: request)

        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            throw ClipSyncError.invalidResponse
        }

        return try JSONDecoder().decode(ClipboardPayload.self, from: data).content
    }

    func pushClipboard(_ text: String) async throws {
        var request = try makeRequest(path: "clipboard", method: "POST")
        request.httpBody = Data(text.utf8)
        let (_, response) = try await URLSession.shared.data(for: request)

        guard let http = response as? HTTPURLResponse, http.statusCode == 204 else {
            throw ClipSyncError.invalidResponse
        }
    }

    func registerDevice(token: String) async throws {
        var request = try makeRequest(path: "devices/register", method: "POST")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try JSONEncoder().encode(["deviceToken": token])
        let (_, response) = try await URLSession.shared.data(for: request)

        guard let http = response as? HTTPURLResponse, http.statusCode == 204 else {
            throw ClipSyncError.invalidResponse
        }
    }
}
