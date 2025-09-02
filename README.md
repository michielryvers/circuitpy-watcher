# CircuitPy Watcher

A tiny CLI that mirrors files between your local workspace and a CircuitPython device over the "Web Workflow" HTTP API. It does an initial full pull, then:

- Watches your local mirror for changes and pushes them to the device
- Periodically polls the device and pulls remote changes
- Handles USB MSC conflicts by pausing local writes and resuming automatically
- Styles console output with Spectre.Console for clear, colorful feedback

> Requires CircuitPython with Web Workflow enabled (Settings -> enable web workflow, set a password).

## Quick start

1) Build

```bash
# From repository root
dotnet build watcher/watcher.csproj -c Release
```

2) Run (replace values as needed)

```bash
./watcher/bin/Release/net10.0/watcher --address http://192.168.1.42 --password YOUR_PASSWORD
```

- `--address` supports host, IP, and optional scheme/port. If you omit the scheme it defaults to `http://`.
- `--password` is the Web Workflow password (basic auth with blank username).

On first start, the tool:

- Prints a banner and connects to your device
- Deletes the local `CIRCUITPYTHON/` folder if it exists
- Performs a full pull of all files into `CIRCUITPYTHON/`
- Starts watching local changes and polling remote changes

Press Ctrl+C to exit cleanly.

## What gets synced?

- Files and folders under the device root `/` are mirrored to the local `CIRCUITPYTHON/` directory by default.
- Local changes are debounced for 500ms (default) and pushed to the device.
- Remote changes are detected via periodic polls (120s by default) and pulled locally.
- Deletions are currently ignored on the local side (local deletes don’t propagate to the device).

Ignored names/extensions (local):

- Directories: `.git`, `.vscode`, `__pycache__`, `.idea`, `node_modules`
- Files: `.DS_Store`, `Thumbs.db`
- Extensions: `.swp`, `.tmp`

You can tweak these in `watcher/src/Core/Ignore.cs`.

## CLI options

```
watcher --address <host[:port]> --password <password>
```

Examples:

```bash
# With IP
./watcher/bin/Release/net10.0/watcher --address 192.168.4.1 --password secret

# With full URL
./watcher/bin/Release/net10.0/watcher --address http://circuitpython.local:80 --password secret
```

## Device conflict handling

When the device is mounted over USB Mass Storage (MSC), writes via the Web API may return HTTP 409 (Conflict). The watcher will:

- Pause local writes and show a yellow PAUSE notice
- Poll the device until it becomes writable again
- Resume queued operations and show a green RESUME notice

## Output styling

The app uses Spectre.Console for a clean, informative TUI:

- Info messages: blue
- Warnings/pauses: yellow
- Success: green
- Errors: red
- Actions/events (PUSH, PULL, MOVE, SKIP): colored tags with concise metadata

See `watcher/src/Tui/ConsoleEx.cs` for formatting helpers.

## Development

Prereqs: .NET 10 (aka .NET 2025/10.0) SDK

- Build: `dotnet build watcher/watcher.csproj`
- Debug run: `./watcher/bin/Debug/net10.0/watcher --address ... --password ...`

### Code formatting (CSharpier)

This project includes the `CSharpier.MsBuild` package, which formats C# on build. If you want to format manually:

```bash
# Format solution (optional if you just build)
dotnet csharpier .
```

You can customize CSharpier with a `.csharpierrc` if desired.

## Troubleshooting

- 401/403 Authentication failed: double-check the Web Workflow password on the device.
- Connection timeouts: ensure the device is reachable and Web Workflow is enabled.
- Address tips: You can pass `--address 192.168.x.y` or `--address http://host:port`. The client normalizes missing schemes to `http://`.
- Permissions on Linux/macOS: if the binary isn’t executable, run `chmod +x watcher/bin/Release/net10.0/watcher`.

## Project layout

- `watcher/` — the .NET console app
  - `src/Config` — configuration model
  - `src/Core` — helpers (ignore list, time setters, path mapping, etc.)
  - `src/Http` — Web Workflow HTTP client
  - `src/Remote` — DTOs for API responses
  - `src/Sync` — full pull, local watcher, remote poller, write coordination
  - `src/Tui` — Spectre.Console helpers
- `CIRCUITPYTHON/` — local mirror folder created/managed by the tool

## License

MIT (c) 2025
