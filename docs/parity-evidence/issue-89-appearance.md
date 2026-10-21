# #89 appearance and validation record

Scope updated per user direction on 2026-10-21: #89 now covers Windows dark
appearance, narrow-window layout and keyboard focus. It no longer requires Mac
runtime captures or source-versus-render annotations, 200% display scaling or
a Narrator pass.

## Windows appearance change

The existing WPF light palette remains unchanged. When Windows app mode is
dark, the app switches its shared surface, text, accent, muted, border and error
brushes to an independently authored dark palette. Generic WPF controls receive
matching dark system brushes; buttons, combo boxes, dropdown items and scroll
bars use the shared palette. The app listens for user preference changes and
reapplies the selected palette. High-contrast mode takes precedence and uses
Windows system brushes; the existing light/dark choice is restored when
high-contrast mode turns off.

## Completed checks

- `tools/build.ps1`: passed, 0 errors. Existing `WaveInEvent`/`WaveOutEvent`
  obsolete warnings and three `CA2022` analyzer warnings remain.
- `tools/check.ps1`: passed, 171 passed, 0 failed, 0 skipped.
- The WPF test checks light/dark switching, system-brush mappings, high-contrast
  precedence, restoration, and the existing synthetic meeting flows.
- Windows synthetic Atlas preview was opened in dark app mode. Home and Settings
  surfaces, enabled/disabled buttons, model selectors, and the selector
  dropdown were inspected. The selected dropdown row uses the dark palette's
  sage highlight and dark text.
- Narrow-window review completed at a 666-pixel screenshot width (the WPF window
  minimum is 680 DIPs). Home content wrapped without horizontal clipping;
  Settings retained its vertical scroll area and visible Cancel/Save actions.
- At normal preview size, Tab moved the visible focus outline from Settings to
  Preview tray menu. UI Automation continued to report Search meetings as the
  focused element for both targets, so its focus signal is inconsistent.
- In a fresh synthetic preview, Tab moved visible keyboard focus to the
  Transcription model combo box. The UI Automation tree exposes its name, but
  the computer-use focus field returned no focused element; it does not
  corroborate the visible focus reliably.
- The UI Automation tree exposed named Settings, Search meetings and Meeting
  app audio controls.
- The synthetic Release preview exposed a dark-theme scrollbar defect: a shared
  14-DIP width and height reduced the vertical bar to a short strip and left
  its line buttons disabled. Sizing the vertical and horizontal orientations
  separately restored the full-height bar; UI Automation enabled the line
  buttons, and clicking Down moved the value 48 to 64 while Up returned it to
  48.
- On this host, `AppsUseLightTheme=0`; the synthetic preview rendered in dark
  mode. The Settings dialog remained scrollable with Cancel and Save visible.
- Preview command: `dotnet run --project Biscotti.Win -- --design-preview=history`.
  The launched executable was
  `Biscotti.Win/bin/Debug/net10.0-windows10.0.17763.0/win-x64/Biscotti.Win.exe`.
- `git diff --check`: passed.

The preview uses only synthetic in-memory data. No recording, model download,
meeting storage or profile changes were made. Windows high-contrast settings
were not changed; high-contrast precedence is covered by the WPF resource test.

Related capability work remains in [#80](https://github.com/d1dac0/biscotti-win/issues/80),
[#81](https://github.com/d1dac0/biscotti-win/issues/81),
[#82](https://github.com/d1dac0/biscotti-win/issues/82),
[#83](https://github.com/d1dac0/biscotti-win/issues/83),
[#84](https://github.com/d1dac0/biscotti-win/issues/84),
[#85](https://github.com/d1dac0/biscotti-win/issues/85),
[#86](https://github.com/d1dac0/biscotti-win/issues/86),
[#87](https://github.com/d1dac0/biscotti-win/issues/87),
[#88](https://github.com/d1dac0/biscotti-win/issues/88) and
[#90](https://github.com/d1dac0/biscotti-win/issues/90). Brand assets remain in
[#53](https://github.com/d1dac0/biscotti-win/issues/53).
