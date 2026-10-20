# Upstream Biscotti Windows clone and port audit

**Checked:** 2026-10-20
**Upstream:** [`scosman/Biscotti` at `f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a`](https://github.com/scosman/Biscotti/tree/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a)

## Executive finding

Cloning the source on Windows is straightforward. Building and running the
existing desktop app on Windows is not a matter of installing one missing
toolchain: the repository is a native macOS 15+ application. Its XcodeGen
project emits Xcode projects, its app targets use Apple frameworks and
entitlements, and model workers are packaged as macOS XPC services. Swift and
Swift Package Manager now have Windows support, but that does not supply
Xcode, Apple SDKs, or Windows implementations of those APIs.

Treat a Windows version as a deliberate port with shared domain logic and
platform-specific adapters. A short Windows clone is useful for source review
or portable Swift package experiments; it will not build the app. The upstream
README also declares PolyForm Perimeter 1.0.1. That license excludes providing
a product that competes with the upstream software, including a port that
substitutes for it. Get rights review before distributing a derivative or
competing Windows product.

## What the repository contains

The audit examined the current tree and these build inputs:

- [`App/project.yml`](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/App/project.yml) declares a macOS 15 app and two macOS 15 XPC service targets. `xcodegen generate` creates the Xcode projects; `xcodebuild` builds and tests them.
- [`Makefile`](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/Makefile) requires Bash, GNU Make conventions and Unix commands (`grep`, `awk`, `curl`, `shasum`, `unzip`, `chmod`). `make bootstrap` calls Homebrew Bundle. The pinned SwiftLint 0.63.3 and SwiftFormat 0.61.1 archives are downloaded and SHA-256 checked by Make rules.
- [`Brewfile`](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/Brewfile) installs XcodeGen and Node.js; it does not install SwiftLint or SwiftFormat because those are fetched at pinned versions by Make.
- `Packages/AudioCapture` implements microphone and system capture against CoreAudio. `Packages/Transcription` depends on Argmax's WhisperKit and SpeakerKit. `Packages/LocalLLM` depends on `llama.swift`; `Packages/BiscottiKit` composes the app's data, calendar, UI, and services. These package manifests specify macOS 15 deployment and contain Apple-platform dependencies.
- `App/Sources` contains the menu-bar app and global hotkey; `XPCServices/BiscottiTranscriber` and `XPCServices/BiscottiLLM` are separately launched macOS workers. `Packages/BiscottiKit` includes calendar and persistence modules built around Apple APIs.
- [`.githooks/pre-commit`](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/.githooks/pre-commit) is a Bash hook that runs formatting, lint, and Swift tests. Hooks are opt-in through `make hooks`.

The checked-in workflow is the source of truth for its exact macOS CI image and
Swift version. Keep a macOS job while the upstream targets depend on Xcode;
Windows CI can be added for portable components and the new Windows app.

## Windows prerequisites

### To clone and inspect only

1. Install Git for Windows and clone to a short path such as `C:\src\Biscotti`.
   The repository has deeply nested Swift package and test paths. Windows still
   has `MAX_PATH` behavior for applications that have not opted into long paths;
   a short checkout avoids relying on every tool in the chain supporting them.
2. Preserve LF for shell scripts and Makefiles. Prefer repository
   `.gitattributes` rules; for a local clone that follows committed line
   endings, `git config core.autocrlf input` avoids converting LF files to
   CRLF on checkout. Set `core.longpaths true` for Git for Windows if path
   errors occur; this does not make every Windows tool long-path aware.

No Swift, Visual Studio, Homebrew, or model runtime is needed just to clone or
read the files.

### To experiment with Windows Swift packages

The official Swift Windows install currently documents Swift 6.4.1 via
WinGet and a Visual Studio C++ toolchain. A baseline x64 setup is:

```powershell
winget install --id Microsoft.VisualStudio.2022.Community --exact --force `
  --custom "--add Microsoft.VisualStudio.Component.Windows11SDK.22621 --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64" `
  --source winget
winget install --id Swift.Toolchain -e --source winget
swift --version
```

Use the current Swift.org installer instructions if the component IDs or
release change. Add Visual Studio's CMake tools or install CMake separately
when building native C/C++ dependencies. Install Node.js only if developing
the repository's Node-based hook tooling. Install a Bash and Make environment
(for example, MSYS2) only for compatible scripts; a Bash shell does not make
Homebrew or `xcodebuild` available.

For optional NVIDIA CUDA development, first confirm the target GPU and
Visual Studio version are supported by the current CUDA release, then install
that matching CUDA Toolkit. CUDA is not required for CPU or DirectML paths.
DirectML requires a DirectX 12 capable device and an ONNX model/opset supported
by its execution provider.

These prerequisites enable Windows-native development and selected portable
Swift packages. They do **not** enable the current macOS app targets. There is
no Windows `xcodebuild` or Windows Apple SDK install that can build this
project unchanged.

## Platform gap and suggested replacements

| Upstream capability | Current platform dependency | Windows direction |
| --- | --- | --- |
| Menu-bar app, settings, global hotkey, notifications | SwiftUI, AppKit, macOS notification and entitlement behavior | Windows desktop shell (WPF/WinUI or another chosen UI), notification-area icon, registered hotkey, Windows notifications, installer and startup policy. Keep the product flow and accessibility requirements in shared specs. |
| Microphone capture | CoreAudio / AVFoundation | WASAPI capture endpoint with device enumeration, permissions/error mapping, resampling, device-change handling, and the same timestamped PCM contract. |
| System audio capture | CoreAudio process/activity monitoring and system capture | WASAPI shared-mode endpoint loopback captures the system mix. For app-only capture, use Windows application loopback and explicitly set the supported minimum Windows version; retain a clear fallback when the target app cannot be isolated. |
| Speech transcription and diarization | WhisperKit, SpeakerKit, Apple acceleration/runtime assumptions | Introduce a backend boundary. Evaluate ONNX Runtime CPU plus DirectML as the broad Windows GPU route; make CUDA an optional NVIDIA route. Validate the exact Whisper and diarization models, operators, latency, memory, and redistribution terms. Do not assume model parity from API compatibility. |
| Local language model | `llama.swift` / Swift binding to llama.cpp | Use a Windows-supported llama.cpp build or another local inference backend behind the same request/streaming contract. Package CPU support first; add CUDA only as an optional tested variant. Preserve local-only processing. |
| Worker isolation | macOS XPC services and launch/entitlement model | Separate Windows worker executables only where crash/resource isolation is valuable. Use local named pipes with current-user ACLs, explicit versioned messages, cancellation, health checks, and deterministic worker exit. |
| Calendar | EventKit and macOS Calendar permission model | Begin with user-selected local `.ics` import to retain offline behavior. Treat Outlook/Microsoft Graph as a separately approved optional integration: it requires Microsoft account authorization and network access, which differs from the upstream local-first promise. |
| Persistence, secrets, file access | Apple persistence/keychain/sandbox conventions | Define a platform-neutral storage contract, then implement local SQLite or a .NET/Windows-native equivalent with explicit schema migrations, per-user ACLs, encrypted secrets, crash-safe writes, and an import/export path. Do not map macOS containers or entitlements directly to Windows. |

For audio, WASAPI loopback is shared-mode capture of a rendering endpoint;
application loopback can isolate a process and its children. They are separate
capabilities, so tests must check both the default system-mix case and the
per-process case. For IPC, named pipes support unrelated local processes, but
the app must restrict remote access and authenticate/authorize the local peer.

## Five epics with proposed user stories

These stories describe a Windows port; they are not proof that the upstream
license permits a competing derivative. Each should be split into its own
tracker issue after product and license review.

### Epic 1 — Developer environment and build infrastructure

#### Story 1.1 — Reproducible Windows developer bootstrap

**As a** Windows contributor, **I want** a versioned bootstrap and setup guide,
**so that** I can prepare a supported machine without guessing which tools the
port expects.

**Acceptance criteria**

- A clean Windows 11 x64 machine can install or validate the pinned Swift,
  Visual Studio C++/Windows SDK, CMake, Git, and optional Node prerequisites.
- The script reports missing components and versions and exits with a clear
  message; it does not silently install optional CUDA or request admin rights
  beyond the documented Visual Studio install.
- All shell scripts/Makefiles retain LF in a fresh Windows clone, and the
  documented short-path clone succeeds.

**Technical notes:** Replace `brew bundle` with a Windows-specific setup path
(WinGet or documented Visual Studio Installer components). Make Bash-only
steps explicit or provide PowerShell equivalents. Pin linter versions and
verify downloaded checksums.

#### Story 1.2 — Windows build and test entry points

**As a** contributor, **I want** one supported Windows build/test command,
**so that** I can distinguish portable components from the Windows app.

**Acceptance criteria**

- `pwsh ./tools/build-windows.ps1` builds supported Windows targets from a
  clean checkout and identifies unsupported macOS targets without attempting
  XcodeGen or `xcodebuild`.
- `pwsh ./tools/check-windows.ps1` runs hardware-free Windows tests and returns
  a failing exit code on build or test failure.
- CI runs those commands on Windows and keeps macOS build/test coverage for the
  still-supported upstream app.

**Technical notes:** First classify packages by platform, add explicit target
conditions, and move reusable logic out of app-target code. Do not declare a
whole package portable until all imports and transitive dependencies build on
Windows.

### Epic 2 — Audio engine abstraction

#### Story 2.1 — Windows microphone and system-mix capture

**As a** Windows meeting participant, **I want** mic and system audio recorded
into the same meeting format, **so that** the rest of the pipeline can process
both channels consistently.

**Acceptance criteria**

- WASAPI enumerates/selects the microphone and render endpoint, captures both
  channels independently, and writes timestamped audio using the shared
  channel contract.
- Permission denial, missing devices, format changes, silence, and device
  removal are surfaced without losing already authenticated audio.
- Hardware-free tests cover state/error transitions; manual tests verify real
  devices, drift, sample-rate conversion, and playback alignment.

**Technical notes:** Use shared-mode WASAPI loopback for the system mix. Match
the existing file format and timestamp semantics before tuning buffer sizes.

#### Story 2.2 — Per-application loopback capture

**As a** user on a call, **I want** to capture only the selected meeting
application, **so that** unrelated computer audio does not enter the transcript.

**Acceptance criteria**

- User can select a running eligible process and see which process tree will be
  included.
- Audio from the selected process and its children is captured; audio from an
  unrelated process is excluded.
- Unsupported Windows versions/apps fall back only with explicit user notice
  and user consent; default system-mix capture remains available.

**Technical notes:** Use Microsoft's application-loopback activation path and
set a minimum supported Windows build based on the implemented API. Test
multi-process apps and process exit/restart.

### Epic 3 — Local AI inference and hardware acceleration

#### Story 3.1 — Windows transcription and speaker backend

**As a** user, **I want** transcription and speaker identification to run
locally on Windows, **so that** meeting audio never needs a cloud service.

**Acceptance criteria**

- The Windows backend transcribes the agreed fixture set on CPU without an
  NVIDIA GPU.
- Speaker timestamps/labels meet documented quality thresholds on a labeled
  evaluation set and retain stable fallback behavior when confidence is low.
- Optional GPU acceleration can be enabled with a supported DirectML device;
  unsupported models/operators fall back to CPU with a visible status.
- Model downloads are lazy, versioned, checksum-verified, resumable, and
  separately checked for redistribution rights.

**Technical notes:** Prototype ONNX Runtime CPU/DirectML before deciding on
CUDA; record accuracy, cold-start, steady-state speed, peak RAM/VRAM, model
size, and minimum Windows/GPU drivers. Keep the existing transcription API
independent of the selected runtime.

#### Story 3.2 — Local LLM backend for Windows

**As a** user, **I want** meeting summaries generated by a local Windows model,
**so that** the Windows build retains offline summarization.

**Acceptance criteria**

- CPU inference can produce a summary from a fixed transcript fixture without
  network access after the model is installed.
- Token streaming, cancellation, context limits, model download integrity, and
  errors use the shared inference contract.
- Optional CUDA acceleration is selected only when the matching runtime and
  GPU are available; CPU remains functional.

**Technical notes:** Compare Windows llama.cpp with DirectML/ONNX options before
selecting the backend. Track model license and quantization separately from
application code.

### Epic 4 — Platform IPC and process lifecycle

#### Story 4.1 — Windows inference worker protocol

**As a** user, **I want** heavy inference isolated in a supervised local
worker, **so that** a model crash or memory spike cannot corrupt the meeting
store or leave the app stuck.

**Acceptance criteria**

- Versioned request/response messages cover transcription, summarization,
  progress, cancellation, and structured errors.
- The main app starts, monitors, cancels, and shuts down the worker; normal app
  exit leaves no worker process or pipe handle behind.
- Local IPC is restricted to the current user and cannot be reached over the
  network.
- Tests cover worker crash, client restart, malformed messages, timeouts, and
  multiple app launches.

**Technical notes:** Prefer .NET named pipes if the Windows app and worker are
.NET. Set current-user ACLs, use an explicit protocol/version, and make all
shutdown paths awaitable.

### Epic 5 — Local storage and Windows OS integration

#### Story 5.1 — Windows meeting storage and secret protection

**As a** user, **I want** meeting records and audio stored safely in my Windows
profile, **so that** data remains local and survives crashes and upgrades.

**Acceptance criteria**

- Meetings, transcript segments, notes, calendar snapshots, and model metadata
  have versioned migrations and crash-safe writes.
- Per-user file permissions are verified; encryption keys are protected with
  Windows user-bound protection (for example DPAPI) and never stored beside
  plaintext secrets.
- Import/export compatibility and recovery from partial/corrupt records are
  covered by tests.

**Technical notes:** Specify schema and key lifetime before choosing SQLite,
EF Core, or another storage implementation. Do not copy macOS data paths,
SwiftData types, entitlements, or Keychain assumptions.

#### Story 5.2 — Offline calendar context

**As a** privacy-conscious user, **I want** to provide local calendar context,
**so that** the app can suggest meetings and speaker names without requiring a
cloud account.

**Acceptance criteria**

- User-selected `.ics` files can be parsed locally for upcoming events and
  attendee names.
- Calendar data remains on-device and is not uploaded; malformed, private, and
  recurring events are handled and tested.
- Any future Outlook/Graph connector is separately opt-in, documents its
  permissions and network behavior, and does not silently replace offline
  behavior.

**Technical notes:** Establish a calendar-provider interface. Treat Microsoft
Graph as a separate network integration, not a transparent EventKit clone.

#### Story 5.3 — Windows tray, notifications, startup, and installer

**As a** user, **I want** the app to fit Windows desktop conventions,
**so that** I can start, control, and close recording predictably.

**Acceptance criteria**

- A notification-area icon exposes recording state and explicit start/stop,
  open, and exit actions.
- Notifications and global hotkeys follow Windows permissions/settings and do
  not claim that a meeting is being recorded when capture has failed.
- Close, tray exit, worker exit, restart, and single-instance behavior leave no
  orphan processes.
- Installer/uninstaller, data retention location, startup behavior, and
  executable signing are documented and verified on a clean Windows VM.

**Technical notes:** Keep global hotkeys opt-in/configurable and notifications
non-sensitive. Define whether closing the window hides to tray or exits before
implementing lifecycle behavior.

## Recommended order

1. Resolve license/rights and define whether the goal is a port, an independent
   clean-room implementation, or a source review only.
2. Complete the Windows build bootstrap and platform boundary before replacing
   audio, inference, persistence, or UI APIs.
3. Deliver CPU-only capture and inference paths before optional GPU backends.
4. Add process isolation and Windows integration after the local meeting flow
   works end to end.

## Sources

- Upstream repository snapshot and build files: [repository](https://github.com/scosman/Biscotti/tree/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a), [Makefile](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/Makefile), [Brewfile](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/Brewfile), [app project](https://github.com/scosman/Biscotti/blob/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/App/project.yml), and [package manifests](https://github.com/scosman/Biscotti/tree/f5f08c9c7cfb9811efcdb666bf65f9085cadcf6a/Packages).
- [Swift for Windows installation](https://www.swift.org/install/windows/) and [Swift platform support](https://www.swift.org/platform-support/).
- [Apple Xcode command-line tools](https://developer.apple.com/documentation/xcode/installing-the-command-line-tools/) and [EventKit access](https://developer.apple.com/documentation/eventkit/accessing-the-event-store).
- Microsoft: [WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording), [application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/), [named pipes](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipes), and [Windows path length limits](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation).
- [ONNX Runtime DirectML](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html), [NVIDIA CUDA for Windows](https://docs.nvidia.com/cuda/cuda-installation-guide-microsoft-windows/index.html), and [CMake on Windows](https://cmake.org/cmake/help/latest/guide/tutorial/Before%20You%20Begin.html).
- [PolyForm Perimeter 1.0.1](https://polyformproject.org/licenses/perimeter/1.0.1) (license notice from upstream README).
