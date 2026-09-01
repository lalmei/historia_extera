// swift-tools-version: 5.10

import PackageDescription

let package = Package(
    name: "HistoriaExteraMac",
    platforms: [
        .macOS(.v14),
    ],
    products: [
        .executable(name: "HistoriaExtera", targets: ["HistoriaExtera"]),
    ],
    targets: [
        .executableTarget(name: "HistoriaExtera"),
    ]
)
