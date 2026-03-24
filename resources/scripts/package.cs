#:package CliWrap@3.10.1

using CliWrap;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

var rid = args.Length > 0 ? args[0] : "win-x64";
var repoRoot = Directory.GetCurrentDirectory();

// Read version from Directory.Build.props
var version = XDocument.Load(Path.Combine(repoRoot, "Directory.Build.props"))
    .Descendants("Version")
    .First()
    .Value;

var buildPath = Path.Combine(repoRoot, "output", "build", rid, "GlazeWM-Tray");
var distDir = Path.Combine(repoRoot, "output", "dist");
var zipFile = Path.Combine(distDir, $"GlazeWM-Tray_{version}_{rid}.zip");

// Clean previous build output
if (Directory.Exists(buildPath))
    Directory.Delete(buildPath, recursive: true);

await Cli.Wrap("dotnet")
    .WithArguments([
        "publish", "src/TrayApp/TrayApp.fsproj",
        "-c", "Release",
        "-r", rid,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-o", buildPath
    ])
    .WithWorkingDirectory(repoRoot)
    .WithStandardOutputPipe(PipeTarget.ToStream(Console.OpenStandardOutput()))
    .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
    .ExecuteAsync();

// Create zip
Directory.CreateDirectory(distDir);
if (File.Exists(zipFile))
    File.Delete(zipFile);
ZipFile.CreateFromDirectory(buildPath, zipFile);