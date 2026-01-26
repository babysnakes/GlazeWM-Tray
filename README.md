## GlazeWM Tray

GlazeWM-Tray is a system tray application to view GlazeWM status and perform some tasks. It's in _beta_ mode. It
currently supports:

- Showing the current active workspace (regardless of the monitor it is on). Only a single lowercase letter/digit is
  displayed - if the name is longer, the first letter/digit is used. The display name (if defined) is displayed in the
  menu / tooltip. If the name does not start with a letter or digit, it's displayed as question mark.
- Indication if GlazeWM is paused.
- Indication if using a custom binding mode.
- Show all active workspaces (in menu) and use this menu to navigate to a workspace.
- Reconnect to GlazeWM (e.g. if GlazeWM quit unexpectedly).
- Refresh it's status - this could happen if you have a shortcut that performs several tasks (e.g. switch to workspace
  and pause) GlazeWM does not send event for every task so some state might be be lost.

### Installation

GlazeWM-Tray can be downloaded from the [releases page][releases]. Once out of beta, it will be available
via [Scoop][] (using my [bucket][]), and hopefully via _winget_.

### Planned Features

The main feature I want to implement is visualisation of currently active workspaces and windows with partial data.
Other than that, I may later implement a free query input with nicely rendered output (e.g. foldable json).

[releases]: https://github.com/babysnakes/glazewm-tray/releases

[Scoop]: https://scoop.sh/

[bucket]: https://github.com/babysnakes/scoop-bucket