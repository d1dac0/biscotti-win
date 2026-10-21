# #89 appearance and validation record

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

- `tools/build.ps1`: passed, 0 errors. The existing `WaveOutEvent` obsolete
  warnings remain.
- `tools/check.ps1`: passed, 171 passed, 0 failed, 0 skipped.
- The WPF test checks light/dark switching, system-brush mappings, high-contrast
  precedence, restoration, and the existing synthetic meeting flows.
- Windows synthetic Atlas preview was opened in dark app mode. Home and Settings
  surfaces, enabled/disabled buttons, model selectors, the selector dropdown,
  and visible keyboard focus were inspected. The selected dropdown row uses the
  dark palette's sage highlight and dark text.
- The UI Automation tree exposed named Settings, Search meetings and Meeting
  app audio controls. This confirms discoverable control names, not a full
  screen-reader pass.
- `git diff --check`: passed.

The preview uses only synthetic in-memory data. No recording, model download,
meeting storage or profile changes were made. Windows high-contrast settings
were not changed; high-contrast precedence is covered by the WPF resource test.

## Checks still requiring a different environment

- The Mac app cannot be run in this Windows environment. The source-derived
  Mac diagrams in the #79 gallery are not runtime screenshots. The requested
  matched Atlas setup/home/recording/review/notes/settings/menu captures and
  source-versus-render annotations remain outstanding until a macOS runtime is
  available.
- 200% display scaling was not exercised. The current Windows display setting
  was left unchanged.
- Narrator/screen-reader behavior was not exercised. UI Automation labels and
  keyboard focus were inspected; that evidence is not represented as a
  screen-reader pass.
- No narrow-window review was completed; the available remote window remained
  at its desktop size during inspection.

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
