## GlazeWM Tray

GlazeWM-Tray is a system tray application to view GlazeWM status and perform some tasks. It's in _beta_ mode. It
currently supports:

- Showing the current active workspace (regardless of the monitor it is on). Only a single lowercase letter/digit is
  displayed - if the name is longer, the first letter/digit is used. The display name (if defined) is displayed in the
  menu / tooltip. If the name does not start with a letter or digit, it's displayed as a question mark.
- Indication if GlazeWM is paused (by grey letter/digit).
- Indication if using a custom binding mode (currently with question mark).
- Show all active workspaces (in menu) and use this menu to navigate to a workspace.
- Reconnect to GlazeWM (e.g. if GlazeWM quit unexpectedly).
- Refresh it's status - this could happen if you have a shortcut that performs several tasks (e.g. switch to workspace
  and pause) GlazeWM does not send event for every task so some state might be be lost.

### Installation

GlazeWM-Tray can be downloaded from the [releases page][releases] as a zip file - make sure you download the
architecture that matches your system. It is also available in my private [Scoop][] [bucket][] (
the [bucket's readme][bucket] contains instructions and lists the available manifests).

If downloaded manually, just extract the zip to some directory, optionally create a link for GlazeWM-Tray.exe and run.
It's also possible to add it to GlazeWM's `startup_commands` and `shutdown_commands` (similar to the _Zebar_ example).

Once running for the first time, open the tray area and drag the icon to the taskbar. It should be visible on the
taskbar from now on.

### Planned Features

The main feature I want to implement is visualisation of currently active workspaces and windows with partial data.
Other than that, I may later implement a free query input with nicely rendered output (e.g. foldable json).

### Contributing

Except for the usual contributions (code, bug reports, docs, etc.), I'm looking for help with design (icons, GUI) and
usability. Please open an issue if you are willing to help.

[releases]: https://github.com/babysnakes/glazewm-tray/releases

[Scoop]: https://scoop.sh/

[bucket]: https://github.com/babysnakes/scoop-bucket