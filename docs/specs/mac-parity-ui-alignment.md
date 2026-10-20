# Mac parity through native Windows screens

**Status:** Accepted

## Decision

For #79, align the hierarchy and interaction sequence of the existing WPF
home, meeting review, settings and audio-device screens. Use independently
authored WPF resources for warm neutral surfaces, sage primary actions,
restrained recording/error red, consistent spacing and Windows-installed
fonts. Use a serif display heading with Segoe UI body text, subject to rendered
verification. Preserve native window chrome, keyboard focus, high-contrast
legibility and existing control behavior. Organize setup, recording, upcoming
meetings and history into clear groups; make Settings accessible from the main
window. Distinguish empty, working and failed states with actionable wording.
Keep the live transcript easy to reach and retain all existing notes/audio
controls. The user approved continuing autonomously on 2026-10-20 after reviewing
the scoped plan.

## Alternatives

A new navigation shell, complete onboarding wizard and Mac feature port would
make this story much larger and mix UI alignment with new capture/model/data
capabilities. File substantial capability gaps as separate stories instead.
Keeping scattered per-window styles would leave the current hierarchy and
consistency problems unresolved.

## Why

The Mac source has a shared design system, grouped home sections and explicit
meeting processing states. Windows already has many equivalent workflows but
exposes them through loosely grouped controls and a Settings dialog reachable
from the tray. Shared resources and a bounded layout pass can address the
visible mismatch without changing the local-only service architecture.

The reference's LICENSE.md at
`f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a` is PolyForm Perimeter 1.0.1,
including a Noncompete clause. This plan authorizes no copying of Mac source,
artwork or bundled fonts. Asset rights for #53 remain that issue's concern.
