# MCP meeting access uses a separate local stdio process

**Status:** Accepted

## Decision

Expose meeting data through a separate `Biscotti.Mcp` console executable using the official Model Context Protocol C# SDK and stdio transport. Its tools are read-only and read only `meeting.json` metadata from the local meeting store; transcript retrieval is paginated and bounded. The MCP reader must not trigger audio migration, open WAV files, or access remote services.

## Alternatives

- **Add an MCP command-line mode to the WPF executable** — couples protocol process lifetime and standard streams to desktop startup and shutdown.
- **Implement JSON-RPC directly** — duplicates protocol negotiation and tool schema behavior already provided by the official SDK.

## Why

- Stdio is the local process transport required by issue #8, and a console process has a clear lifetime controlled by the MCP client.
- The separate process references `Biscotti.Core` and reads the same local store without starting audio capture, notifications, or the WPF tray app.
- Read-only tools and metadata-only access preserve the local-only data promise and avoid side effects from legacy audio migration during a query.
