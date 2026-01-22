set shell := ["powershell", "-Command"]

version := `([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version`
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

[doc('Restore from scratch honering the lock file')]
restore:
    dotnet tool restore
    dotnet restore

[doc('Run CI checks and tests')]
ci: restore check-format
    dotnet build
    dotnet test

[doc('Generate icons from PNG images (exported from Affinity)')]
[working-directory('resources')]
icons:
    .\gen-icons.ps1
    mv generated/*.ico ../src/TrayApp/Assets/

[doc("Package the applicationm for distribution")]
package rid=defaultRID:
    dotnet publish ./src/TrayApp/TrayApp.fsproj \
        -c Release \
        -r {{ rid }} \
        --self-contained true \
        -p:PublishSingleFile=true \
        -o dist/{{rid}}/GlazeWM-Tray-{{ version }}
