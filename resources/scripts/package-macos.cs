#:package CliWrap@3.10.1

using CliWrap;
using System.IO;
using System.Xml.Linq;

var rid = args.Length > 0 ? args[0] : "osx-arm64";
var repoRoot = Directory.GetCurrentDirectory();

// Read version from Directory.Build.props
var version = XDocument.Load(Path.Combine(repoRoot, "Directory.Build.props"))
    .Descendants("Version")
    .First()
    .Value;

var publishDir  = Path.Combine(repoRoot, "output", "build", rid, "publish");
var appBundle   = Path.Combine(repoRoot, "output", "build", rid, "GlazeWM-Tray.app");
var distDir     = Path.Combine(repoRoot, "output", "dist");
var zipFile     = Path.Combine(distDir, $"GlazeWM-Tray_{version}_{rid}.zip");
var iconsetDir  = Path.Combine(repoRoot, "resources", "macos", "icon.iconset");
var icnsFile    = Path.Combine(repoRoot, "output", "build", rid, "icon.icns");

async Task Run(string cmd, string[] cmdArgs) =>
    await Cli.Wrap(cmd)
        .WithArguments(cmdArgs)
        .WithWorkingDirectory(repoRoot)
        .WithStandardOutputPipe(PipeTarget.ToStream(Console.OpenStandardOutput()))
        .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
        .ExecuteAsync();

// Generate icon
Console.WriteLine("[INFO] Generating icon.icns...");
Directory.CreateDirectory(Path.GetDirectoryName(icnsFile)!);
await Run("iconutil", ["-c", "icns", iconsetDir, "-o", icnsFile]);

// Clean previous build
if (Directory.Exists(publishDir)) Directory.Delete(publishDir, recursive: true);
if (Directory.Exists(appBundle))  Directory.Delete(appBundle,  recursive: true);

// Publish
Console.WriteLine($"[INFO] Publishing for {rid}...");
await Run("dotnet", [
    "publish", "src/TrayApp/TrayApp.fsproj",
    "-c", "Release",
    "-r", rid,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-o", publishDir
]);

// Build .app bundle structure
Console.WriteLine("[INFO] Assembling .app bundle...");
var contentsDir  = Path.Combine(appBundle, "Contents");
var macosDir     = Path.Combine(contentsDir, "MacOS");
var resourcesDir = Path.Combine(contentsDir, "Resources");
Directory.CreateDirectory(macosDir);
Directory.CreateDirectory(resourcesDir);

// Copy all published output (handles any native dylibs alongside the single-file exe)
foreach (var entry in Directory.EnumerateFileSystemEntries(publishDir, "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(publishDir, entry);
    var dest = Path.Combine(macosDir, relative);
    if (Directory.Exists(entry))
        Directory.CreateDirectory(dest);
    else
        File.Copy(entry, dest);
}

// Ensure the executable bit is set
if (OperatingSystem.IsMacOS())
{
    File.SetUnixFileMode(
        Path.Combine(macosDir, "GlazeWM-Tray"),
        UnixFileMode.UserRead  | UnixFileMode.UserWrite  | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
}

// Copy icon
File.Copy(icnsFile, Path.Combine(resourcesDir, "icon.icns"));

// Write Info.plist (fill version into template)
var plistTemplate = File.ReadAllText(Path.Combine(repoRoot, "resources", "macos", "Info.plist"));
File.WriteAllText(Path.Combine(contentsDir, "Info.plist"), plistTemplate.Replace("{{VERSION}}", version));

// Ad-hoc codesign
Console.WriteLine("[INFO] Signing (ad-hoc)...");
await Run("codesign", ["--force", "--deep", "--sign", "-", appBundle]);

// Zip using ditto (preserves macOS metadata / resource forks)
Console.WriteLine("[INFO] Creating zip...");
Directory.CreateDirectory(distDir);
if (File.Exists(zipFile)) File.Delete(zipFile);
await Run("ditto", ["-c", "-k", "--sequesterRsrc", "--keepParent", appBundle, zipFile]);

Console.WriteLine($"[INFO] Done → {zipFile}");
