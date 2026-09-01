import Foundation

@MainActor
final class ViewerServer: ObservableObject {
    enum Phase: Equatable {
        case idle
        case starting
        case ready
        case failed(String)
    }

    static let shared = ViewerServer()

    let viewerURL = URL(string: "http://127.0.0.1:4321/")!

    @Published private(set) var phase: Phase = .idle
    @Published private(set) var log = ""
    @Published private(set) var generation = UUID()
    @Published private(set) var revealLocation: URL?

    private var process: Process?
    private var outputPipe: Pipe?
    private var startupTask: Task<Void, Never>?
    private var launchID = UUID()
    private var ownsProcess = false

    private init() {}

    func start() {
        guard phase == .idle || isFailed else { return }

        phase = .starting
        log = ""
        launchID = UUID()
        let thisLaunch = launchID

        startupTask = Task { [weak self] in
            guard let self else { return }

            if await self.viewerIsReady() {
                self.appendLog("Using the Historia Extera viewer already running on port 4321.\n")
                self.ownsProcess = false
                self.markReady(for: thisLaunch)
                return
            }

            do {
                let runtime = try RuntimeLayout.find()
                self.revealLocation = runtime.revealLocation
                try self.launchViewer(runtime: runtime, launchID: thisLaunch)
                await self.waitUntilReady(launchID: thisLaunch)
            } catch {
                self.fail(error.localizedDescription, for: thisLaunch)
            }
        }
    }

    func restart() {
        stop()
        phase = .idle
        start()
    }

    func stop() {
        launchID = UUID()
        startupTask?.cancel()
        startupTask = nil

        outputPipe?.fileHandleForReading.readabilityHandler = nil
        outputPipe = nil

        if ownsProcess, let process, process.isRunning {
            process.terminate()
        }

        process = nil
        ownsProcess = false
    }

    private var isFailed: Bool {
        if case .failed = phase { return true }
        return false
    }

    private func launchViewer(runtime: RuntimeLayout, launchID: UUID) throws {
        let child = Process()
        let pipe = Pipe()

        child.executableURL = runtime.node
        child.arguments = [
            runtime.astro.path,
            "dev",
            "--host",
            "127.0.0.1",
            "--port",
            "4321",
        ]
        child.currentDirectoryURL = runtime.viewerRoot
        child.standardOutput = pipe
        child.standardError = pipe

        var environment = ProcessInfo.processInfo.environment
        let inheritedPath = environment["PATH"] ?? "/usr/bin:/bin:/usr/sbin:/sbin"
        var executablePaths = [runtime.node.deletingLastPathComponent().path]
        if let dotnet = runtime.dotnet {
            executablePaths.append(dotnet.deletingLastPathComponent().path)
        }
        executablePaths.append(inheritedPath)
        environment["PATH"] = executablePaths.joined(separator: ":")
        environment["ASTRO_TELEMETRY_DISABLED"] = "1"
        environment["HISTORIA_NATIVE_APP"] = "1"
        // Astro detects Codex as an agent and otherwise detaches its dev server. Mark this
        // process as the already-supervised child so it stays attached to the macOS app.
        environment["ASTRO_DEV_BACKGROUND"] = "1"
        if let cli = runtime.cli {
            environment["HISTORIA_CLI"] = cli.path
            environment["HISTORIA_MODULE_DIR"] = runtime.astro
                .deletingLastPathComponent()
                .deletingLastPathComponent()
                .deletingLastPathComponent()
                .path
        }
        if let worldDirectory = runtime.worldDirectory {
            environment["HISTORIA_WORLD_DIR"] = worldDirectory.path
        }
        if let trashDirectory = runtime.trashDirectory {
            environment["HISTORIA_TRASH_DIR"] = trashDirectory.path
        }
        if let cacheDirectory = runtime.viteCacheDirectory {
            environment["HISTORIA_CACHE_DIR"] = cacheDirectory.path
        }
        child.environment = environment

        pipe.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let data = handle.availableData
            guard !data.isEmpty, let text = String(data: data, encoding: .utf8) else { return }
            Task { @MainActor [weak self] in
                guard let self, self.launchID == launchID else { return }
                self.appendLog(text)
            }
        }

        child.terminationHandler = { [weak self] process in
            Task { @MainActor [weak self] in
                guard let self, self.launchID == launchID else { return }
                self.process = nil
                self.ownsProcess = false
                self.fail("The viewer process exited with status \(process.terminationStatus).", for: launchID)
            }
        }

        appendLog("Starting the viewer from \(runtime.viewerRoot.path)…\n")
        try child.run()
        process = child
        outputPipe = pipe
        ownsProcess = true
    }

    private func waitUntilReady(launchID: UUID) async {
        for _ in 0..<120 {
            guard !Task.isCancelled, self.launchID == launchID else { return }

            if await viewerIsReady() {
                markReady(for: launchID)
                return
            }

            try? await Task.sleep(for: .milliseconds(250))
        }

        fail("The viewer did not answer on port 4321 within 30 seconds.", for: launchID)
    }

    private func viewerIsReady() async -> Bool {
        var request = URLRequest(url: viewerURL)
        request.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        request.timeoutInterval = 1

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { return false }
            return String(decoding: data.prefix(32_768), as: UTF8.self).contains("Historia Extera")
        } catch {
            return false
        }
    }

    private func markReady(for launchID: UUID) {
        guard self.launchID == launchID else { return }
        phase = .ready
        generation = UUID()
    }

    private func fail(_ message: String, for launchID: UUID) {
        guard self.launchID == launchID else { return }
        phase = .failed(message)
    }

    private func appendLog(_ text: String) {
        log.append(text)
        if log.count > 24_000 {
            log.removeFirst(log.count - 24_000)
        }
    }
}

private struct RuntimeLayout {
    let viewerRoot: URL
    let node: URL
    let dotnet: URL?
    let cli: URL?
    let astro: URL
    let worldDirectory: URL?
    let trashDirectory: URL?
    let viteCacheDirectory: URL?
    let revealLocation: URL

    static func find() throws -> RuntimeLayout {
        if let bundled = try bundled() {
            return bundled
        }
        return try developer()
    }

    private static func bundled() throws -> RuntimeLayout? {
        let fileManager = FileManager.default
        guard let resources = Bundle.main.resourceURL else { return nil }

        let bundledRoot = resources.appendingPathComponent("runtime", isDirectory: true)
        let viewerSource = bundledRoot.appendingPathComponent("viewer-source", isDirectory: true)
        let nodeModules = bundledRoot.appendingPathComponent("node_modules", isDirectory: true)
        let node = bundledRoot.appendingPathComponent("bin/node")
        let cli = bundledRoot.appendingPathComponent("bin/legends")
        let bundledAstro = nodeModules.appendingPathComponent("astro/bin/astro.mjs")
        let cacheIDFile = bundledRoot.appendingPathComponent("viewer-cache-id")

        let markers = [viewerSource, nodeModules, node, cli, bundledAstro, cacheIDFile]
        guard markers.allSatisfy({ fileManager.fileExists(atPath: $0.path) }) else {
            return nil
        }

        let cacheID = try String(contentsOf: cacheIDFile, encoding: .utf8)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        let allowedCacheIDCharacters = CharacterSet.alphanumerics.union(
            CharacterSet(charactersIn: "-_")
        )
        guard !cacheID.isEmpty,
              cacheID.unicodeScalars.allSatisfy(allowedCacheIDCharacters.contains)
        else {
            throw AppStartupError("The bundled viewer cache identifier is invalid.")
        }

        let cacheBase = try fileManager.url(
            for: .cachesDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        ).appendingPathComponent("HistoriaExtera", isDirectory: true)
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
        let cacheKey = "\(version)-\(cacheID)"
        let runtimeRoot = cacheBase.appendingPathComponent("runtime-\(cacheKey)", isDirectory: true)
        let viewerRoot = runtimeRoot.appendingPathComponent("viewer", isDirectory: true)
        let cachedModules = runtimeRoot.appendingPathComponent("node_modules", isDirectory: true)

        // Vite emits dependency paths into browser module URLs. Copy dependencies to a stable
        // cache path without spaces so the app still works from `Historia Extera.app`; reuse the
        // versioned copy on later launches. Astro's smaller source workspace is refreshed.
        try fileManager.createDirectory(at: runtimeRoot, withIntermediateDirectories: true)
        if !fileManager.fileExists(atPath: cachedModules.path) {
            try fileManager.copyItem(at: nodeModules, to: cachedModules)
        }
        if fileManager.fileExists(atPath: viewerRoot.path) {
            try fileManager.removeItem(at: viewerRoot)
        }
        try fileManager.copyItem(at: viewerSource, to: viewerRoot)
        try fileManager.createSymbolicLink(
            at: viewerRoot.appendingPathComponent("node_modules"),
            withDestinationURL: cachedModules
        )

        let viteCache = cacheBase.appendingPathComponent("vite-\(cacheKey)", isDirectory: true)
        try fileManager.createDirectory(at: viteCache, withIntermediateDirectories: true)

        let applicationSupport = try fileManager.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        ).appendingPathComponent("Historia Extera", isDirectory: true)
        let worlds = applicationSupport.appendingPathComponent("Worlds", isDirectory: true)
        let trash = applicationSupport.appendingPathComponent("Trash", isDirectory: true)
        try fileManager.createDirectory(at: worlds, withIntermediateDirectories: true)
        try fileManager.createDirectory(at: trash, withIntermediateDirectories: true)

        return RuntimeLayout(
            viewerRoot: viewerRoot,
            node: node,
            dotnet: nil,
            cli: cli,
            astro: cachedModules.appendingPathComponent("astro/bin/astro.mjs"),
            worldDirectory: worlds,
            trashDirectory: trash,
            viteCacheDirectory: viteCache,
            revealLocation: applicationSupport
        )
    }

    private static func developer() throws -> RuntimeLayout {
        let root = try RepositoryLocator.find()
        guard let node = executable(named: "node") else {
            throw AppStartupError("Node.js 22.12 or newer was not found. Install Node, then try again.")
        }
        guard let dotnet = executable(named: "dotnet") else {
            throw AppStartupError("The .NET 10 SDK was not found. Install it, then try again.")
        }

        let astro = root.appendingPathComponent("viewer/node_modules/astro/bin/astro.mjs")
        guard FileManager.default.fileExists(atPath: astro.path) else {
            throw AppStartupError(
                "Viewer dependencies are not installed. Run `make install` in the repository, then try again."
            )
        }

        return RuntimeLayout(
            viewerRoot: root.appendingPathComponent("viewer", isDirectory: true),
            node: node,
            dotnet: dotnet,
            cli: nil,
            astro: astro,
            worldDirectory: nil,
            trashDirectory: nil,
            viteCacheDirectory: nil,
            revealLocation: root
        )
    }

    private static func executable(named name: String) -> URL? {
        let environment = ProcessInfo.processInfo.environment
        let fileManager = FileManager.default
        var directories = (environment["PATH"] ?? "")
            .split(separator: ":")
            .map(String.init)

        directories.append(contentsOf: [
            "/opt/homebrew/bin",
            "/usr/local/bin",
            "/usr/local/share/dotnet",
            "/usr/bin",
        ])

        if let home = environment["HOME"] {
            directories.append("\(home)/.dotnet")
            directories.append("\(home)/.volta/bin")

            let nvm = URL(fileURLWithPath: home)
                .appendingPathComponent(".nvm/versions/node", isDirectory: true)
            if let versions = try? fileManager.contentsOfDirectory(
                at: nvm,
                includingPropertiesForKeys: nil,
                options: [.skipsHiddenFiles]
            ) {
                directories.append(contentsOf: versions.map { $0.appendingPathComponent("bin").path })
            }
        }

        for directory in directories.reversed() {
            let candidate = URL(fileURLWithPath: directory, isDirectory: true).appendingPathComponent(name)
            if fileManager.isExecutableFile(atPath: candidate.path) {
                return candidate
            }
        }

        return nil
    }
}

private enum RepositoryLocator {
    static func find() throws -> URL {
        let environment = ProcessInfo.processInfo.environment
        let fileManager = FileManager.default
        var candidates: [URL] = []

        if let configured = environment["HISTORIA_EXTERA_ROOT"], !configured.isEmpty {
            candidates.append(URL(fileURLWithPath: configured, isDirectory: true))
        }

        // `make macos-app` places the bundle in <repository>/build/.
        candidates.append(
            Bundle.main.bundleURL
                .deletingLastPathComponent()
                .deletingLastPathComponent()
        )

        candidates.append(URL(fileURLWithPath: fileManager.currentDirectoryPath, isDirectory: true))

        // Useful when running the Swift package directly during development.
        candidates.append(
            URL(fileURLWithPath: #filePath)
                .deletingLastPathComponent()
                .deletingLastPathComponent()
                .deletingLastPathComponent()
                .deletingLastPathComponent()
        )

        for candidate in candidates {
            let root = candidate.standardizedFileURL
            let viewer = root.appendingPathComponent("viewer/astro.config.mjs")
            let engine = root.appendingPathComponent("src/HistoryEngine.Cli/HistoryEngine.Cli.csproj")
            if fileManager.fileExists(atPath: viewer.path), fileManager.fileExists(atPath: engine.path) {
                return root
            }
        }

        throw AppStartupError(
            "Could not find the Historia Extera repository. Build the app with `make macos-app`, " +
                "or set HISTORIA_EXTERA_ROOT before launching it."
        )
    }
}

private struct AppStartupError: LocalizedError {
    let message: String

    init(_ message: String) {
        self.message = message
    }

    var errorDescription: String? { message }
}
