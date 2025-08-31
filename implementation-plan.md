# Implementation Plan

## What we’re building
We’re creating a console tool that mirrors a CircuitPython device’s filesystem to a local `./CIRCUITPYTHON` folder and keeps it in sync while running. On startup, the tool deletes any existing local mirror and performs a fresh full pull from the device. During steady state, it watches the local folder for changes and pushes updates to the device, while periodically polling the device for remote changes to pull them down. It honors the Web Workflow Filesystem REST API semantics and constraints.

### Reference documents in this repo
- `circuitpython-filesystem-api.md` — Concise API reference for the CircuitPython Web Workflow Filesystem REST API: endpoints, methods, headers, status codes, and practical notes for syncing.
- `watcher-flow.md` — End-to-end behavior spec for the console tool: startup, steady-state sync, rename handling, polling, pause/resume on USB MSC, error handling, and logging format.

## Tech stack
- .NET 10 console application (C#)
  - Reasoning: Cross-platform, strong HTTP, file system watchers, structured logging, and solid async primitives.
- Polly for resiliency
  - Use for transient HTTP fault handling (retry with exponential backoff), and pausing/resume logic orchestration.

## High-level architecture
- HTTP Client Layer
  - Auth, request building, retries via Polly, JSON parsing, streaming GET/PUT, MOVE.
- Filesystem Model & Diff
  - Types to represent remote entries (dir/file, size, modified_ns), local entries (size, mtime), and comparison utilities (timestamp+size).
- Sync Engine
  - Initial full pull, local watcher event processing with debounce, remote periodic polling, and queued write operations with paused mode on 409.
- Ignore & Path Normalization
  - Apply default local ignore patterns; normalize device API paths (trailing slash for dirs).
- Logging
  - Structured, single-line actions as specified.

## Incremental task checklist

1) Repo bootstrap
- [x] Add a new .NET 10 console project (e.g., `watcher`) and solution file.
- [x] Add package references (Polly).
- [x] Set up Directory.Build.props for C# language version and nullable enable.

2) Domain contracts and constants
- [ ] Create config model (address, password, local root path, poll intervals, debounce, etc.) consistent with watcher-flow.md.
- [ ] Define constants for default ignores and API paths.

3) HTTP client and API wrappers
- [ ] Implement a thin API client for `/cp/version.json`, `/cp/diskinfo.json` (writability), `/fs/<dir>/` GET JSON, file GET, file PUT (with Expect and X-Timestamp), dir PUT, file/dir MOVE, file/dir DELETE (not used yet but useful), with proper Basic auth.
- [ ] Integrate Polly retry policies for transient faults (3 attempts, exponential backoff).
- [ ] Surface typed results (status codes, payloads) and helpers to interpret common conditions (401/403/404/409).

4) Path normalization and mapping
- [ ] Implement utilities to map between local paths and remote `/fs` paths, ensuring directory trailing slash rules.
- [ ] Implement ignore checks and symlink skipping.

5) Full pull (Device → Local)
- [ ] Recursive directory listing and download using API; write files and set local mtime from `modified_ns`.
- [ ] Sequential (no concurrency) transfers; robust error handling and retries.

6) Local watcher
- [ ] Initialize file system watcher on `./CIRCUITPYTHON`.
- [ ] Debounce per path (500ms).
- [ ] For each effective event, refresh remote metadata for the path/parent and decide action by timestamp+size (equal → no-op).
- [ ] Implement PUSH logic (ensure dirs exist via PUT directory; then PUT file with Expect + X-Timestamp).
- [ ] Implement MOVE attempt on rename; fallback to PUT new + DELETE old.

7) Remote polling (Device → Local)
- [ ] Full-tree metadata poll every 120s; compare with local and pull newer remote files.
- [ ] Ignore deletions from remote.

8) Paused state on 409 (USB MSC)
- [ ] When a write returns 409, enter paused state; queue subsequent writes.
- [ ] Poll writability every 5s via `/cp/diskinfo.json` or directory GET `writable`.
- [ ] On writable=true, resume and drain the queue in order.

9) Error handling and exits
- [ ] Exit immediately on 401/403 at startup.
- [ ] Apply Polly retry for transient faults everywhere else; auto-resume after failures.

10) Logging
- [ ] Implement the specified single-line action log format (PULL, PUSH, MOVE, SKIP, PAUSE, RESUME, ERROR) with reasons.

11) CLI wiring
- [ ] Parse `--address` and `--password` (minimal CLI).
- [ ] Construct base URL and auth from inputs; validate with `/cp/version.json`.

12) Smoke tests and manual validation
- [ ] Add a small test harness or script to simulate API responses (optional).
- [ ] Manual test: initial run with a device; verify full pull, local edit push, remote poll pull, pause/resume on 409.

13) Docs and guardrails
- [ ] Update README with prerequisites, how to run, and safety notes (local folder deletion on startup).
- [ ] Re-check `watcher-flow.md` and `circuitpython-filesystem-api.md` alignment with implementation.

14) Nice-to-haves (later)
- [ ] Dry-run mode; structured JSON logs.
- [ ] Configurable ignore patterns; parallel transfers; temp-file + MOVE atomic pushes.
- [ ] Hash-based comparisons; selective subtree sync; more CLI options.
