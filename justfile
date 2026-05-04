[doc('Run console against running GlazeWM')]
[working-directory('src/cli/')]
cli:
    dotnet run

[doc('Run console against mock websocket (port 8181)')]
[working-directory('src/cli/')]
cli-mock:
    dotnet run -- 8181

format:
    dotnet fantomas .

check-format:
    dotnet fantomas --check .

lint:
    dotnet fsharplint lint GlazeWM-Tray.sln

check: check-format lint

[doc('Verbose testing')]
vtest:
    dotnet test -- NUnit.ConsoleOut=1

[doc('Restore from scratch honering the lock file')]
restore:
    dotnet tool restore

[doc('Run CI checks and tests')]
ci: restore check
    dotnet build
    dotnet test

[doc('Generate icons from PNG images (exported from Affinity)')]
icons:
    dotnet run resources/scripts/gen-icons.cs

[windows]
[doc("Package the application for distribution")]
package rid='win-x64':
    dotnet run resources/scripts/package-windows.cs -- {{ rid }}

[macos]
[doc("Package the application for distribution")]
package rid='osx-arm64':
    dotnet run resources/scripts/package-macos.cs -- {{ rid }}

[windows]
[doc("Distribute all supported architectures")]
dist: (package "win-x64") (package "win-arm64")

[macos]
[doc("Distribute all supported architectures")]
dist: (package "osx-arm64") (package "osx-x64")
