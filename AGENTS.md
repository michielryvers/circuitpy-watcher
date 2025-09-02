# Repository Guidelines

## Project Structure & Module Organization
- `watcher/`: .NET console app (entry: `Program.cs`).
  - `src/Config`: app settings (`AppConfig`).
  - `src/Http`: Web Workflow client (`WebWorkflowClient`, `ApiResult`).
  - `src/Sync`: full pull, local watcher, remote poller, write coordinator.
  - `src/Core`: helpers (path mapping, ignores, file times).
  - `src/Remote`: DTOs for device API.
  - `src/Tui`: console output helpers (Spectre.Console).
- `CIRCUITPYTHON/`: local mirror folder created at runtime; deleted on startup.

## Build, Test, and Development Commands
- Build: `dotnet build watcher/watcher.csproj -c Release`
- Run (debug): `dotnet run --project watcher -- --address 192.168.4.1 --password *****`
- Run (built): `./watcher/bin/Release/net10.0/watcher --address http://host --password *****`
- Format (CSharpier): `dotnet csharpier .` (also runs on build via MSBuild package)

## Coding Style & Naming Conventions
- Language: C# (nullable + implicit usings enabled; LangVersion `latest`).
- Indentation: 4 spaces; braces on new line per C# conventions.
- Namespaces: `Watcher.*` by folder (e.g., `Watcher.Sync`).
- Types/members: `PascalCase`; locals/parameters: `camelCase`.
- One public type per file; file name matches type.
- HTTP headers/paths centralized in `Core/ApiConstants.cs`.

## Testing Guidelines
- No unit test project yet; rely on manual smoke tests:
  - First run deletes `./CIRCUITPYTHON` then performs a full pull.
  - Edit a local file under `CIRCUITPYTHON/` → expect `PUSH`.
  - Modify a file on device → expect `PULL` on poll.
  - With USB MSC active, writes return 409 → expect `PAUSE` then `RESUME` and queued flush.
- Optional next step: add xUnit tests for `Core` (path mapping, ignores) and `Http` using fakes.

## Commit & Pull Request Guidelines
- Use Conventional Commits where possible: `feat:`, `fix:`, `chore:`, `docs:`.
- Prefer imperative, concise subjects; reference tasks when relevant (e.g., `feat: Task 6 local watcher`).
- PRs should include:
  - Clear description and rationale.
  - How to run/reproduce (commands and sample CLI output lines like `PUSH`, `PULL`).
  - Linked issues and any screenshots of console output when useful.

## Security & Configuration Tips
- The Web Workflow uses HTTP Basic; avoid sharing real passwords in examples or logs.
- You may pass `--address host[:port]`; scheme is normalized to `http://` if omitted.
- Never commit credentials; keep device passwords out of scripts and history.

