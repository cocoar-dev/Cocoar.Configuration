# Changelog

## [Unreleased]
### Added
- Concise fluent overloads for File and Environment providers:
	- `Rule.From.File(string filePath, string? configurationPath = null)`
	- `Rule.From.Environment(string? environmentPrefix = null)`
	These complement existing factory forms allowing dynamic option/query computation.

### Documentation
- Updated provider READMEs (File, Environment) with overload summaries.
- Updated main `README.md` Quick Start + examples (`BasicUsage`, `FileLayering`, `AspNetCoreExample`, `ServiceLifetimes`) to showcase concise overloads.

### Notes
- Factory lambda forms remain for advanced scenarios (dynamic debounce, conditional section selection, custom option shaping).

## [0.9.1] - 2025-09-14
Branding / assets update.

- Replaced NuGet package icon (`package-icon.png`).
- Updated README image.
- Updated GitHub social preview images (`social-preview.png`, `social-preview-small.png`) stored at repo root.
- No functional/code changes.

## [0.9.0] - 2025-09-14
Initial release 🎉

- Deterministic ordered configuration layering (last-write-wins)
- Strongly typed DI (no IOptions<T>)
- Providers: File, Environment, Static, HTTP Polling, Microsoft Adapter
- Dynamic rule factories & atomic snapshot recompute
- DI lifetimes & keyed registrations
- Examples included under `src/Examples/`
