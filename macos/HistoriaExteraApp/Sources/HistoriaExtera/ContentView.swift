import AppKit
import SwiftUI
import WebKit

struct ContentView: View {
    @EnvironmentObject private var server: ViewerServer
    @StateObject private var browser = BrowserModel()

    var body: some View {
        Group {
            switch server.phase {
            case .idle, .starting:
                startupView
            case .ready:
                browserView
            case .failed(let message):
                failureView(message)
            }
        }
        .toolbar {
            if server.phase == .ready {
                ToolbarItemGroup(placement: .navigation) {
                    Button {
                        browser.goBack()
                    } label: {
                        Label("Back", systemImage: "chevron.left")
                    }
                    .disabled(!browser.canGoBack)

                    Button {
                        browser.goForward()
                    } label: {
                        Label("Forward", systemImage: "chevron.right")
                    }
                    .disabled(!browser.canGoForward)

                    Button {
                        browser.reload()
                    } label: {
                        Label("Reload", systemImage: "arrow.clockwise")
                    }
                }

                ToolbarItem(placement: .status) {
                    Label("Local engine", systemImage: "circle.fill")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .symbolRenderingMode(.palette)
                        .foregroundStyle(.green, .secondary)
                }
            }
        }
        .onChange(of: server.generation) {
            guard server.phase == .ready else { return }
            browser.load(server.viewerURL)
        }
    }

    private var startupView: some View {
        VStack(spacing: 18) {
            ProgressView()
                .controlSize(.large)
            Text("Starting Historia Extera")
                .font(.title2.weight(.semibold))
            Text("Preparing the local generator and world viewer…")
                .foregroundStyle(.secondary)

            if !server.log.isEmpty {
                ScrollView {
                    Text(server.log)
                        .font(.system(.caption, design: .monospaced))
                        .foregroundStyle(.secondary)
                        .textSelection(.enabled)
                        .frame(maxWidth: 720, alignment: .leading)
                }
                .frame(maxHeight: 180)
                .padding(12)
                .background(.quaternary, in: RoundedRectangle(cornerRadius: 8))
            }
        }
        .padding(36)
    }

    private var browserView: some View {
        BrowserView(model: browser)
            .onAppear {
                if browser.webView.url == nil {
                    browser.load(server.viewerURL)
                }
            }
    }

    private func failureView(_ message: String) -> some View {
        VStack(alignment: .leading, spacing: 18) {
            Label("The local viewer did not start", systemImage: "exclamationmark.triangle.fill")
                .font(.title2.weight(.semibold))
                .foregroundStyle(.orange)

            Text(message)
                .foregroundStyle(.secondary)
                .textSelection(.enabled)

            if !server.log.isEmpty {
                ScrollView {
                    Text(server.log)
                        .font(.system(.caption, design: .monospaced))
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .frame(minHeight: 120, maxHeight: 300)
                .padding(12)
                .background(.quaternary, in: RoundedRectangle(cornerRadius: 8))
            }

            HStack {
                Button("Try Again") {
                    server.restart()
                }
                .keyboardShortcut(.defaultAction)

                if let revealLocation = server.revealLocation {
                    Button("Show Files") {
                        NSWorkspace.shared.activateFileViewerSelecting([revealLocation])
                    }
                }
            }
        }
        .frame(maxWidth: 760, alignment: .leading)
        .padding(36)
    }
}

@MainActor
final class BrowserModel: NSObject, ObservableObject, WKNavigationDelegate {
    let webView: WKWebView

    @Published private(set) var canGoBack = false
    @Published private(set) var canGoForward = false

    override init() {
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .default()
        webView = WKWebView(frame: .zero, configuration: configuration)
        super.init()
        webView.navigationDelegate = self
        webView.allowsMagnification = true
    }

    func load(_ url: URL) {
        webView.load(URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData))
    }

    func reload() {
        webView.reloadFromOrigin()
    }

    func goBack() {
        webView.goBack()
    }

    func goForward() {
        webView.goForward()
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        updateNavigationState(webView)
    }

    func webView(
        _ webView: WKWebView,
        decidePolicyFor navigationAction: WKNavigationAction,
        decisionHandler: @escaping (WKNavigationActionPolicy) -> Void
    ) {
        if navigationAction.navigationType == .linkActivated,
           let url = navigationAction.request.url,
           !Self.isLocal(url)
        {
            NSWorkspace.shared.open(url)
            decisionHandler(.cancel)
            return
        }

        decisionHandler(.allow)
    }

    private func updateNavigationState(_ webView: WKWebView) {
        canGoBack = webView.canGoBack
        canGoForward = webView.canGoForward
    }

    private static func isLocal(_ url: URL) -> Bool {
        url.host == "127.0.0.1" || url.host == "localhost"
    }
}

struct BrowserView: NSViewRepresentable {
    let model: BrowserModel

    func makeNSView(context: Context) -> WKWebView {
        model.webView
    }

    func updateNSView(_ nsView: WKWebView, context: Context) {}
}
