defaultRID := 'win-x64'

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
    dotnet restore

[doc('Run CI checks and tests')]
ci: restore check
    dotnet build
    dotnet test

[doc('Generate icons from PNG images (exported from Affinity)')]
icons:
    dotnet run resources/scripts/gen-icons.cs

[doc("Package the applicationm for distribution")]
package rid=defaultRID:
    dotnet run resources/scripts/package.cs -- {{ rid }}

[doc("Distribute all supported architectures")]
dist: (package "win-x64") (package "win-arm64")
