# Local MCP server

`Biscotti.Mcp` is a separate stdio process for MCP clients. It exposes saved
meeting data from `%LOCALAPPDATA%\BiscottiWin\Meetings` using three read-only
tools:

- `search-meetings` searches titles, summaries, notes, action items, transcript
  text, and speakers. An empty query lists recent meetings.
- `get-meeting` returns the meeting summary, notes, action items, attendees,
  timestamped notes, and transcript line count for a meeting GUID.
- `get-transcript` returns speaker-labeled transcript lines with start and end
  recording timestamps. Use `offset` and `limit` to retrieve long transcripts
  in pages.

The server reads meeting metadata only. It does not start the desktop app,
capture audio, read WAV files, modify meetings, or connect to remote services.
Meeting metadata is currently stored as plaintext; see the
[DPAPI storage decision](specs/dpapi-meeting-data-at-rest.md).

## Build

From a PowerShell prompt in the repository root:

```powershell
dotnet build -c Release Biscotti.Mcp\Biscotti.Mcp.csproj
```

## Configure an MCP client

Configure the client to launch the built `Biscotti.Mcp.exe` as a stdio server.
For a source checkout, a client configuration entry has this shape (replace the
path with the absolute path to the checkout):

```json
{
  "mcpServers": {
    "biscotti": {
      "command": "C:\\path\\to\\BiscottiWin\\Biscotti.Mcp\\bin\\Release\\net10.0-windows\\win-x64\\Biscotti.Mcp.exe",
      "args": []
    }
  }
}
```

The client starts the server when connecting and stops it when the connection
closes. Do not launch multiple server processes for the same client entry.
