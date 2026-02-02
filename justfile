set shell := ["powershell", "-NoProfile", "-Command"]

build_dir := "output/build"
dist_dir := "output/dist"
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

[doc('Verbose testing')]
vtest:
	dotnet test -- NUnit.ConsoleOut=1

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
    #!powershell -NoProfile
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    Remove-Item generated/*.ico,../src/TrayApp/Assets/*.ico

    $chars = [string[]]([char[]](97..122) + (0..9) + "qm")

    foreach ($i in $chars)
    {
        magick generated/icon-$i-b_16.png generated/icon-$i-b_32.png generated/icon-$i-b.ico
        magick generated/icon-$i-w_16.png generated/icon-$i-w_32.png generated/icon-$i-w.ico
        magick generated/icon-$i-g_16.png generated/icon-$i-g_32.png generated/icon-$i-g.ico
    }

    magick generated/icon_16.png generated/icon_32.png generated/icon_64.png generated/icon_128.png generated/icon.ico
    magick generated/error_16.png generated/error_32.png generated/error.ico
    mv generated/*.ico ../src/TrayApp/Assets/

[doc("Package the applicationm for distribution")]
package rid=defaultRID:
    #!powershell -NoProfile
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest

    $buildPath = "{{ build_dir }}/{{ rid }}/GlazeWM-Tray"
    if (Test-Path $BuildPath) { rm $BuildPath -Recurse }
    dotnet publish ./src/TrayApp/TrayApp.fsproj `
        -c Release `
        -r {{ rid }} `
        --self-contained true `
        -p:PublishSingleFile=true `
        -o $buildPath

    New-Item -ItemType Directory -Force -Path '{{ dist_dir }}' | Out-Null
    $zipFile = "{{ dist_dir }}/GlazeWM-Tray_{{ version }}_{{ rid }}.zip"
    if (Test-Path $zipFile) { rm $zipFile -Recurse }
    Compress-Archive -Path $buildPath -DestinationPath $zipFile

[doc("Distribute all supported architectures")]
dist: (package "win-x64") (package "win-arm64")
