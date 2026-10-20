# Tray, hotkey, notification, and single-instance behavior

**Status:** In Progress

## Decision

Use a Windows Forms `NotifyIcon` hosted by the WPF application for a persistent tray presence. Show `Idle`, `Recording`, and `Processing` states, with a context menu for Start, Stop, Open last meeting, Settings, and Exit. Closing the main window hides it to the tray; Exit is explicit. Tray actions call the same recording and meeting commands as the main window, so they cannot create a second recording workflow.

Register a configurable global shortcut with Win32 `RegisterHotKey` after the main window has a handle. Default to Ctrl+Alt+R. Persist the key and modifiers in the existing `settings.json`; provide a Settings dialog to change it. Report registration conflicts in Settings and keep the shortcut inactive until the user selects an available combination. Unregister it during shutdown.

Use the Windows App SDK `AppNotificationManager` local app notification API for the summary-ready notification. Send an ordinary, non-urgent notification and rely on Windows notification settings and Focus Assist / Do not disturb to suppress it. Do not use a balloon-tip or custom-window fallback that could interrupt the user when Windows suppresses notifications. Keep notification text generic and local; do not include transcript or summary content. Bundle the Windows App SDK runtime with the app output so unpackaged users do not need to install a separate runtime.

Enforce one app instance per Windows user with a named mutex whose name includes the current user SID. A second launch sends an `Activate` command to the primary instance over a local named pipe protected with `PipeOptions.CurrentUserOnly`; the primary restores and focuses its existing window. The secondary exits without creating services or starting capture. If the pipe is not ready immediately after mutex acquisition, retry briefly before exiting with a clear startup error.

## Alternatives

- A custom WPF tray icon would require reimplementing shell notification-area behavior; `NotifyIcon` is the built-in Windows desktop facility and fits this WPF app.
- A low-level keyboard hook would see more keystrokes and require broader lifecycle handling; `RegisterHotKey` is purpose-built and reports collisions when another app already owns the chord.
- A legacy `ToastNotificationManager` desktop toast requires a registered Start-menu shortcut and AppUserModelID. The current Windows guidance recommends `AppNotificationManager` for WPF and unpackaged desktop applications.
- A named-window search or a mutex alone can detect another process, but cannot reliably deliver an activation request to it. A current-user-only local pipe provides that small IPC path without broadening access to other users.

## Why

The tray keeps recording controls available while the main window is hidden. A single command path keeps hotkey, tray, and window actions consistent. Windows-owned notifications let the operating system apply the user's notification and Focus Assist policy. The mutex prevents duplicate recorders, while the local pipe gives the existing process a precise activation signal.

## Implementation boundaries

- Preserve the existing local-only data guarantee; no meeting content is sent to notification services.
- Tray state follows the existing meeting service state: `Idle` → `Recording` → `Processing` → `Idle`.
- Start and Stop remain unavailable when the meeting service is in an incompatible state; the tray menu mirrors those enabled states.
- Integrate window actions with the per-app audio selector introduced by issue #14. Do not modify that selector's capture policy as part of issue #18.
- Register and unregister global shortcut and pipe resources deterministically during startup and shutdown.
- Keep app notification delivery ordinary priority; do not request elevated/urgent notification behavior that can bypass Focus Assist.

## Verification plan

- Unit-test shortcut settings serialization and tray/meeting state-to-menu action availability where those rules can be kept independent of Windows shell APIs.
- Build and run `tools/build.ps1` and `tools/check.ps1`.
- Manually verify tray states and menu commands through idle, recording, processing, and completion; verify Ctrl+Alt+R starts/stops; hold the chord in another process and confirm the Settings conflict message; launch the app twice and confirm the second launch focuses the first without creating another recorder; verify summary notification appears normally and is suppressed by Windows Focus Assist / Do not disturb.
- Verify the existing app-window controls, including the issue #14 audio app selector, remain available and cannot be changed during recording.

## References

- [Windows notifications overview](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/)
- [App notifications with a .NET WPF app](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [Named pipe security and access rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)
- GitHub issue [#18](https://github.com/d1dac0/biscotti-win/issues/18)
- GitHub issue [#14](https://github.com/d1dac0/biscotti-win/issues/14)
