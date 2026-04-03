#:package CliWrap@3.10.1

using CliWrap;
using System.IO;

Directory.SetCurrentDirectory(Path.Combine(Directory.GetCurrentDirectory(), "resources"));

// Clean up existing ico files
foreach (var f in Directory.GetFiles("generated", "*.ico"))
    File.Delete(f);
foreach (var f in Directory.GetFiles(Path.Combine("..", "src", "TrayApp", "Assets"), "*.ico"))
    File.Delete(f);

// a-z, 0-9, qm (question mark)
var chars = Enumerable.Range('a', 26).Select(c => ((char)c).ToString())
    .Concat(Enumerable.Range(0, 10).Select(i => i.ToString()))
    .Append("qm");

async Task Magick(string[] args) =>
    await Cli.Wrap("magick")
        .WithArguments(args)
        .WithStandardOutputPipe(PipeTarget.ToStream(Console.OpenStandardOutput()))
        .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
        .ExecuteAsync();

foreach (var c in chars)
{
    await Magick([$"generated/icon-{c}-b_16.png", $"generated/icon-{c}-b_32.png", $"generated/icon-{c}-b.ico"]);
    await Magick([$"generated/icon-{c}-w_16.png", $"generated/icon-{c}-w_32.png", $"generated/icon-{c}-w.ico"]);
    await Magick([$"generated/icon-{c}-g_16.png", $"generated/icon-{c}-g_32.png", $"generated/icon-{c}-g.ico"]);
}

await Magick(["generated/icon_16.png", "generated/icon_32.png", "generated/icon_64.png", "generated/icon_128.png", "generated/icon.ico"]);
await Magick(["generated/error_16.png", "generated/error_32.png", "generated/error.ico"]);

// Move generated icos to Assets
var assetsDir = Path.Combine("..", "src", "TrayApp", "Assets");
foreach (var f in Directory.GetFiles("generated", "*.ico"))
    File.Move(f, Path.Combine(assetsDir, Path.GetFileName(f)));

// Populate macOS iconset for iconutil
var iconsetDir = Path.Combine("macos", "icon.iconset");
Directory.CreateDirectory(iconsetDir);

var iconsetFiles = new (string src, string dest)[]
{
    ("icon_16.png",   "icon_16x16.png"),
    ("icon_32.png",   "icon_16x16@2x.png"),
    ("icon_32.png",   "icon_32x32.png"),
    ("icon_64.png",   "icon_32x32@2x.png"),
    ("icon_128.png",  "icon_128x128.png"),
    ("icon_256.png",  "icon_128x128@2x.png"),
    ("icon_256.png",  "icon_256x256.png"),
    ("icon_512.png",  "icon_256x256@2x.png"),
    ("icon_512.png",  "icon_512x512.png"),
    ("icon_1024.png", "icon_512x512@2x.png"),
};

foreach (var (src, dest) in iconsetFiles)
    File.Copy(Path.Combine("generated", src), Path.Combine(iconsetDir, dest), overwrite: true);