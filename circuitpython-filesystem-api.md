# CircuitPython Web Workflow – Filesystem REST API Reference

This document summarizes the HTTP filesystem API exposed by CircuitPython’s Web Workflow. It’s designed for building reliable sync tooling (device ↔ local) without browsing the docs.

Source: CircuitPython Workflows (Web → File REST API)


## Base URL and Auth
- Base: http://<host>/ (commonly http://circuitpython.local, redirected to http://cpy-XXXXXX.local)
- Filesystem API prefix: /fs
- HTTP 1.1; responses may be chunked.
- Auth: HTTP Basic (user empty, password from settings.toml key CIRCUITPY_WEB_API_PASSWORD)
  - 401 Unauthorized: wrong/missing password
  - 403 Forbidden: password not set on the device

Common constraints
- When USB MSC is active, write operations are blocked (returns 409 Conflict). Reads still work.
- Directory paths must end with a trailing slash /. File paths must not.
- All endpoints support OPTIONS for CORS. For API clients, the important header is:
  - Access-Control-Allow-Methods: GET, OPTIONS when USB active; otherwise GET, OPTIONS, PUT, DELETE, MOVE.
- Timestamps
  - Optional X-Timestamp header (milliseconds since Unix epoch) sets file/dir modified time. RTC time used otherwise.
- Rename/Move
  - Use HTTP method MOVE with header X-Destination: <absolute path under /fs>.


## Directory endpoints

### GET /fs/<directory path>/
Returns JSON describing the directory.

Status codes
- 200 OK: Success
- 401 Unauthorized | 403 Forbidden | 404 Not Found

Response JSON
- free: number (free blocks on the disk of this directory)
- total: number (total blocks on that disk)
- block_size: number (bytes per block)
- writable: boolean (true if Web Workflow can write; false if USB has the disk)
- files: array of file entries
  - name: string (no trailing / for dirs)
  - directory: boolean (true for directories)
  - modified_ns: number (mtime in nanoseconds since Unix epoch; resolution may be less than ns)
  - file_size: number (bytes; 0 for directories)

Example Accept header to request JSON from the HTML browser route: Accept: application/json


### PUT /fs/<directory path>/
Create the directory if it doesn’t exist. Body is ignored. Optional X-Timestamp to set mtime (ms epoch).

Status codes
- 201 Created: Newly created
- 204 No Content: Already exists (dir or file at that path)
- 401 Unauthorized | 403 Forbidden | 404 Not Found (missing parent) | 409 Conflict (USB active) | 500 Server Error


### MOVE /fs/<directory path>/
Rename or move directory to the destination path.

Headers
- X-Destination: /fs/<new directory path>/ (must end with /)

Status codes
- 201 Created: Renamed
- 401 Unauthorized | 403 Forbidden | 404 Not Found (source missing or destination header missing) | 409 Conflict (USB active) | 412 Precondition Failed (destination exists)


### DELETE /fs/<directory path>/
Recursively delete the directory and all its contents.

Status codes
- 204 No Content: Deleted
- 401 Unauthorized | 403 Forbidden | 404 Not Found | 409 Conflict (USB active)


## File endpoints

### PUT /fs/<file path>
Create or overwrite a file with the request body. Optional X-Timestamp to set mtime (ms epoch). Large file handling uses Expect: 100-continue.

Status codes
- 201 Created: New file created
- 204 No Content: Existing file overwritten
- 401 Unauthorized | 403 Forbidden | 404 Not Found (missing parent) | 409 Conflict (USB active) | 413 Payload Too Large (no Expect used) | 417 Expectation Failed (Expect used and too large) | 500 Server Error

Behavior
- If client sends Expect: 100-continue and size is acceptable, server responds 100 Continue before upload proceeds.


### GET /fs/<file path>
Download the raw file contents.

Status codes
- 200 OK: Success
- 401 Unauthorized | 403 Forbidden | 404 Not Found

Content-Type (by extension)
- text/plain: .py, .txt
- text/javascript: .js
- text/html: .html
- application/json: .json
- application/octet-stream: everything else


### MOVE /fs/<file path>
Rename or move a file to the destination path.

Headers
- X-Destination: /fs/<new file path>

Status codes
- 201 Created: Renamed
- 401 Unauthorized | 403 Forbidden | 404 Not Found (source missing or destination header missing) | 409 Conflict (USB active) | 412 Precondition Failed (destination exists)


### DELETE /fs/<file path>
Delete the file.

Status codes
- 204 No Content: Deleted
- 401 Unauthorized | 403 Forbidden | 404 Not Found | 409 Conflict (USB active)


## Service discovery and device info (adjacent APIs useful for sync tooling)

Although not part of /fs itself, these endpoints help orchestrate syncing:

- GET /cp/version.json
  - web_api_version (1–4), version, build_date, board_name, mcu_name, board_id, creator_id, creation_id, hostname, port, ip
- GET /cp/diskinfo.json
  - List of disks with: root, free, total, block_size, writable
- GET /cp/devices.json
  - Discover other CircuitPython devices on the network via MDNS


## Error handling summary
- 401 Unauthorized: Wrong/missing password. Provide Basic auth with blank username and the configured password.
- 403 Forbidden: Web API password not set on the device.
- 404 Not Found: Path missing (file/dir) or parent missing for creation.
- 409 Conflict: USB MSC has claimed the disk, write ops blocked.
- 412 Precondition Failed: Destination already exists for MOVE.
- 413/417: Payload too large handling based on Expect header usage.
- 500 Server Error: Unhandled error; retry or inspect device logs.


## Notes for building a robust sync
- Always normalize paths: directories require trailing slash; files must not.
- Use Accept: application/json when listing directories to avoid HTML.
- For timestamp preservation, send X-Timestamp (ms) on PUT for files and directories.
- Detect writability via either directory GET.writable or /cp/diskinfo.json; if false, back off and prompt to eject USB MSC.
- Prefer MOVE for renames to preserve metadata; falls back to copy+delete if needed.
- Consider reading modified_ns and file_size to quickly compare trees and detect changes.
- Consider web_api_version for compatibility; directory JSON changed in v4 (now an object with fields and files array).
- CORS only matters for browser clients. CLI tools can ignore.
