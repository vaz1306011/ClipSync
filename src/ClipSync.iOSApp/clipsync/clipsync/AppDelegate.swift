import UIKit

/// Handles APNs registration and the silent push that wakes this app from
/// the background to pull the latest clipboard content from the Windows server.
final class AppDelegate: NSObject, UIApplicationDelegate {
    func application(
        _ application: UIApplication,
        didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
    ) -> Bool {
        application.registerForRemoteNotifications()
        return true
    }

    func application(_ application: UIApplication, didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data) {
        let tokenString = deviceToken.map { String(format: "%02x", $0) }.joined()
        UserDefaults.standard.set(tokenString, forKey: "deviceToken")

        Task {
            try? await ClipSyncClient.shared.registerDevice(token: tokenString)
        }
    }

    func application(_ application: UIApplication, didFailToRegisterForRemoteNotificationsWithError error: Error) {
        print("APNs registration failed: \(error)")
    }

    func application(
        _ application: UIApplication,
        didReceiveRemoteNotification userInfo: [AnyHashable: Any],
        fetchCompletionHandler completionHandler: @escaping (UIBackgroundFetchResult) -> Void
    ) {
        Task {
            do {
                let content = try await ClipSyncClient.shared.fetchLatestClipboard()
                await MainActor.run {
                    UIPasteboard.general.string = content
                }
                completionHandler(.newData)
            } catch {
                completionHandler(.failed)
            }
        }
    }
}
