// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Tietie",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "Tietie",
            path: "Sources/Tietie",
            linkerSettings: [.linkedLibrary("sqlite3")]
        )
    ]
)
