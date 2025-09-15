# Changelog

## [Unreleased]

### Breaking
- Removed: Query-level `configurationPath` (File / HTTP Polling) replaced by rule-level `.Select(...)`.
- Removed: (Earlier) query-level `targetPath` (now fully migrated to `.MountAt(...)`; note for upgrades from <0.9.2).

### Added
- Added: Rule-level `.Select(path)` to extract a subsection prior to mounting / merging.
- Added: Centralized pipeline (Fetch → Select → Mount → Merge) documented; providers now always return full root JSON.

### Changed
- Simplified provider query option signatures (no path parameters; only identity + provider-specific timing/debounce fields).
- Centralized selection & mounting logic inside `RuleManager` using `JsonPath.SelectColonDelimited`.

### Migration
```diff
- Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("appsettings.json", configurationPath: "A:B")).For<MyCfg>().Build();
+ Rule.From.File("appsettings.json").Select("A:B").For<MyCfg>().Build();

- Rule.From.HttpPolling(_ => HttpPollingRuleOptions.FromPath("service.json", configurationPath: "Service")).For<Service>().Build();
+ Rule.From.HttpPolling("service.json").Select("Service").For<Service>().Build();

- Rule.From.File(_ => FileSourceRuleOptions.FromFilePath("base.json", targetPath: "Config:Base"))...
+ Rule.From.File("base.json").MountAt("Config:Base")...
```

If you previously used an experimental `.Pick(...)`, rename it to `.Select(...)`.

## [0.9.2] - 2025-09-15

- Added: Concise overloads Rule.From.File(...), Rule.From.Environment(...).
- Added: .MountAt fluent API for rule mounting.
- Migration: Replace targetPath: "A:B" with .MountAt("A:B").

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
