//
//  ContentView.swift
//  clipsync
//
//  Created by sora on 2026/9/14.
//

import SwiftUI

struct ContentView: View {
    @AppStorage("serverBaseURL") private var serverBaseURL = ""
    @AppStorage("pairingKey") private var pairingKey = ""
    @State private var statusMessage = ""
    @State private var isSyncing = false

    var body: some View {
        NavigationStack {
            Form {
                Section("伺服器設定") {
                    TextField("http://192.168.x.x:8787", text: $serverBaseURL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .keyboardType(.URL)
                    SecureField("配對金鑰", text: $pairingKey)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                }

                Section("手動測試") {
                    Button("立即從伺服器拉取") { syncNow() }
                        .disabled(isSyncing)

                    Button("把目前剪貼板內容送出") { pushNow() }
                        .disabled(isSyncing)

                    if !statusMessage.isEmpty {
                        Text(statusMessage)
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .navigationTitle("ClipSync")
        }
    }

    private func syncNow() {
        isSyncing = true
        Task {
            defer { isSyncing = false }
            do {
                let content = try await ClipSyncClient.shared.fetchLatestClipboard()
                UIPasteboard.general.string = content
                statusMessage = "已更新剪貼板"
            } catch {
                statusMessage = "失敗:\(error.localizedDescription)"
            }
        }
    }

    private func pushNow() {
        guard let text = UIPasteboard.general.string else {
            statusMessage = "剪貼板是空的"
            return
        }

        isSyncing = true
        Task {
            defer { isSyncing = false }
            do {
                try await ClipSyncClient.shared.pushClipboard(text)
                statusMessage = "已上傳"
            } catch {
                statusMessage = "失敗:\(error.localizedDescription)"
            }
        }
    }
}

#Preview {
    ContentView()
}
