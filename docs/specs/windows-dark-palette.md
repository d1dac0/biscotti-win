# Windows dark palette follows the Windows app theme

**Status:** Accepted

## Decision

Add an independently authored dark resource palette for the existing WPF
surfaces and select it from the Windows app light/dark preference. Windows
high-contrast resources take precedence over both palettes and continue to
track system colors dynamically. Do not add a separate Biscotti theme setting
or alter native window chrome in this change.

## Alternatives

A Biscotti-specific theme selector adds a new setting and persistence behavior
for a parity task that asks for a Windows palette. A fixed dark theme ignores
the user's Windows choice. Replacing native window chrome would widen the work
and change established Windows keyboard and accessibility behavior.

## Why

The #79 evidence already verifies the light palette and dynamic high-contrast
brush switching. A system-following dark palette extends the existing resource
model while keeping Windows high contrast authoritative and avoids introducing
another local preference. Microsoft documents `AppsUseLightTheme` as the
Windows app color mode and recommends handling color-mode changes during a
desktop app session. The Mac remains a reference only; this decision authorizes
no copying of source, artwork or bundled fonts.

Reference: [Windows 10/11 settings registry values](https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common), [support dark and light themes in Win32 desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/apply-windows-themes).

## Validation plan

- Add WPF resource tests for light, dark and high-contrast precedence, including
  restoration when the system mode changes.
- Review the synthetic Atlas screens at 100% and 200% scaling and at a narrow
  window width; inspect keyboard focus and accessible names with Windows UI
  accessibility tools. Record untested hardware-dependent checks explicitly.
- Attach actual matched-fixture macOS runtime captures only when a macOS runtime
  is available. Source-derived diagrams are not runtime captures and cannot
  satisfy that acceptance criterion.
