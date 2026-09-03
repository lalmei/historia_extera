import AppKit
import SwiftUI

@main
struct HistoriaExteraApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var server = ViewerServer.shared

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(server)
                .frame(minWidth: 980, minHeight: 680)
                .task {
                    server.start()
                }
        }
        .defaultSize(width: 1440, height: 900)
        .windowStyle(.hiddenTitleBar)
        .commands {
            CommandGroup(after: .toolbar) {
                Button("Restart Local Viewer") {
                    server.restart()
                }
                .keyboardShortcut("r", modifiers: [.command, .shift])
            }
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationWillTerminate(_ notification: Notification) {
        ViewerServer.shared.stop()
    }
}
