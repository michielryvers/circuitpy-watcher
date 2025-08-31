# Watcher Flow (Console Tool)

This document describes the behavior of the console tool that mirrors a CircuitPython device filesystem to a local folder and keeps it in sync while running. It defines inputs, startup behavior, steady-state syncing, conflict policy, error handling, and logging. No code here—only the agreed flow.


## Intent and scope
- Provide a simple developer experience: run the tool, point to a device, get a fresh local mirror in `./CIRCUITPYTHON`, and keep the device in sync with local edits.
- Simplified startup: if `./CIRCUITPYTHON` exists, delete it (recursively) without confirmation, then perform a fresh full pull from the device.
- While running, reflect local modifications to the device. Periodically pull remote changes. Ignore deletions for now.


## Inputs
- Required CLI args:
  - `--address` (host or IP; may include `:port`, default port 80 if omitted)
  - `--password` (HTTP Basic password; username is blank)


## Pre-flight and connection
- Accept hostname (e.g., `cpy-XXXXXX.local`) or raw IP; optional `:port`.
- Authentication failures:
  - 401 Unauthorized or 403 Forbidden → exit immediately with a clear message.
- Transient network failures/timeouts:
  - Use resilience: up to 3 retries with exponential backoff for individual operations; auto-resume overall operation after transient failures.


## Local mirror location
- Local root: `./CIRCUITPYTHON` (relative to current working directory).
- On startup:
  - If the folder exists, delete it recursively without confirmation.
  - Create a fresh empty `./CIRCUITPYTHON`.


## Exclusions and special files
- Local → Device: skip typical junk and tool artifacts by default:
  - `.git/`, `.vscode/`, `__pycache__/`, `.DS_Store`, `Thumbs.db`, `*.swp`, `*.tmp`, `.idea/`, `node_modules/`
- Skip local symlinks (do not dereference or upload).
- Device → Local: include everything exposed by the `/fs` API (no remote ignores).


## Path and time conventions
- Directory paths in the device API end with `/`; file paths do not.
- Comparisons use:
  - Timestamp: modified time (remote `modified_ns` → local mtime ms; local mtime → `X-Timestamp` ms for pushes)
  - Size: `file_size` in bytes
- Equality rule for no-op:
  - If timestamp and size are equal, do nothing.


## Startup sequence
1) Validate input args; build base URL (`http://<address>`), derive port if provided.
2) Test connectivity/auth by calling `GET /cp/version.json`:
   - If 401/403 → exit.
   - On transient failure → retry (3 attempts with exponential backoff).
3) Prepare local root:
   - Delete existing `./CIRCUITPYTHON` recursively.
   - Create fresh `./CIRCUITPYTHON`.
4) Perform initial full pull (see Full pull algorithm) to populate the local mirror.
5) Start the steady-state sync loop:
   - File watcher on `./CIRCUITPYTHON` with 500ms debounce per path.
   - Periodic remote poll every 120s (full-tree metadata).
   - Writability monitor every 5s when paused due to USB MSC.


## Full pull algorithm (Device → Local)
- Recursively list remote directories via `GET /fs/<dir>/` with `Accept: application/json`.
- For each directory entry:
  - If directory: create corresponding local dir; recurse.
  - If file: download via `GET /fs/<file>`; write to local path; set local mtime to remote `modified_ns` (converted to ms).
- No parallelism (single-threaded transfers).


## Steady-state sync

### Local changes (Local → Device)
- Triggered by file watcher events (create/modify/rename/move) under `./CIRCUITPYTHON`.
- Debounce per path for 500ms to collapse rapid changes.
- Before acting on an event, refresh the latest remote metadata for the affected path (or parent directory if needed) using the `/fs` API.
- Decide action using timestamp+size:
  - If local is newer than remote (by mtime) or size differs in a way that indicates change → PUSH to device (PUT file; ensure parent dirs exist via PUT on directories).
  - If remote is newer → PULL from device and overwrite local.
  - If equal → no-op.
- Moves/renames:
  - Attempt `MOVE` on the device when the local watcher reports a rename; if unavailable or detected as delete+create, fallback to `PUT` new then `DELETE` old on device.
  - Directory moves supported analogously.
- On pushes (PUT): always send `Expect: 100-continue` and set `X-Timestamp` from local mtime (ms).

### Periodic remote poll (Device → Local)
- Every 120s, recursively list the entire remote tree.
- For each remote file/dir:
  - If not present locally → PULL and create locally.
  - If present locally → compare timestamp+size; if remote newer → PULL; if equal or older → no-op.
- Deletions are ignored (do not delete locally if missing remotely).

### Deletions (ignored for now)
- Local deletions: do not propagate to device.
- Remote deletions: do not delete locally; next periodic pull will not re-add missing remote entries.


## Writability and paused state (USB MSC active)
- When a write operation returns `409 Conflict`, the device is not writable (USB mass storage holds the disk).
- Behavior:
  - Enter paused sync state; queue subsequent write operations for later.
  - Continue to service read-only operations (e.g., GET for remote poll).
  - Every 5 seconds, check writability via either `GET /cp/diskinfo.json` or directory `GET` response `writable` flag.
  - When writable becomes true, automatically resume and drain the queued operations in order.


## Error handling and resilience
- Transient HTTP/network failures (timeouts, connection reset, 5xx):
  - Retry the individual operation up to 3 times with exponential backoff (e.g., 0.5s, 1s, 2s), then surface an error but keep the tool running and auto-resume on next trigger/poll.
- Authentication errors (401/403):
  - Exit immediately with a clear message.
- Not found (404) on expected paths:
  - For pushes: ensure parent directories exist (PUT dir) and retry once; otherwise log and skip.
- Payload size (413/417):
  - Always send `Expect: 100-continue` to avoid 413; if 417 occurs, log and skip.


## Logging output (no verbosity levels)
- One concise line per action:
  - `PULL  <path>  (reason: remote-newer | missing-local)`
  - `PUSH  <path>  (reason: local-newer | missing-remote)`
  - `MOVE  <from> -> <to>  (reason: local-rename)`
  - `DELETE <path>  (skipped by policy)`
  - `SKIP  <path>  (ignored: pattern | equal | symlink)`
  - `PAUSE (USB MSC active) – waiting for writable…`
  - `RESUME (writable)`
  - `ERROR <path>  (message)`


## Performance characteristics
- No parallel transfers (sequential operations).
- Always use `Expect: 100-continue` for PUT.
- 500ms debounce for local events.
- Remote full-tree polling every 120s.
- Writability check every 5s when paused.


## Security notes
- HTTP Basic over HTTP; password in plaintext per Web Workflow design. Do not reuse sensitive passwords.


## Out of scope (future enhancements)
- Propagating deletions with tombstones/state tracking.
- Atomic uploads via temp path + MOVE.
- Parallel transfers and smarter batching.
- Configurable ignore sets and verbosity levels.
- Hash-based change detection (beyond timestamp+size).
- Selective subtree sync.
