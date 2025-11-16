# AGENTS.md — AI Assistant Guidance for .NET Repositories

> **System prompt for GitHub Copilot and other AI assistants:**
> Act as a context-aware assistant for this repository.
> Your responsibilities: ensure high-quality C#/.NET code, maintain consistency, and prepare the repository for release.
> Always prioritize clarity, intent, and maintainability over verbosity.
> Follow the principles below and extend them as this repository evolves.

---

## 🎯 Purpose

This file defines how AI assistants (e.g., GitHub Copilot) work with this repository.
It establishes the philosophy, quality standards, and release-readiness criteria that define a polished .NET project.

---

## 🧭 Core Principles

* **Explain Why, Not What** — Comments describe *intent and reasoning*, not code behavior.
* **Consistency Over Novelty** — Prefer predictable patterns that align with the repository's style.
* **Simplicity and Intent** — Code should be self-explanatory and purposeful.
* **Incremental Improvement** — Every change should leave the codebase cleaner and more consistent.
* **Automation Friendly** — Prefer patterns that integrate easily with CI/CD and documentation tooling.

---

## 💬 Comment Policy: Why, Not What

Code comments should explain *why* decisions were made, not *what* the code does. Self-documenting code (clear naming, structure) is always preferable to comments.

### ✅ Keep These Comments

* **Rationale for non-obvious choices** — Why a specific algorithm, library, or approach was chosen
* **Performance trade-offs** — Justification for optimizations or deliberate inefficiencies
* **Security decisions** — Why certain patterns are used for security-sensitive operations
* **Business logic context** — Domain knowledge that isn't obvious from code alone
* **Workarounds** — Why unusual patterns exist to handle external limitations
* **Future considerations** — TODOs with clear context (what, why, when)

**Examples:**
```csharp
// Using SHA256 instead of MD5 because we're hashing sensitive data and MD5 is cryptographically broken
var hasher = SHA256.Create();

// ConfigureAwait(false) to avoid capturing SynchronizationContext - this is a library, not an app
await LoadConfigAsync().ConfigureAwait(false);

// Caching for 5 minutes because the upstream API rate-limits us to 100 requests/hour
_cache.Set(key, value, TimeSpan.FromMinutes(5));
```

### ❌ Remove These Comments

* **Restating the code** — If the code is clear, don't repeat it in prose
* **Obvious descriptions** — Explaining what a well-named method does
* **Commented-out code** — Use version control, don't leave dead code
* **Debug/temporary comments** — "test", "TODO: fix this", etc. without context
* **Obsolete explanations** — Comments that no longer match the code
* **Auto-generated noise** — Boilerplate like "Constructor for X"

**Examples to remove:**
```csharp
// Create a hasher
var hasher = SHA256.Create();

// Loop through items
foreach (var item in items) { ... }

// This method gets the configuration
public IConfiguration GetConfiguration() { ... }

// TODO: fix
// var x = 5;
```

### 🔄 Prefer Refactoring Over Comments

When you're tempted to add a comment explaining complex code:
1. **Extract method** — Pull complexity into a well-named method
2. **Rename variables** — Make intent clear through naming
3. **Simplify logic** — Reduce cognitive load
4. Only add a comment if the *why* still isn't obvious

---

## 🧠 Context & Awareness

When assisting, AI assistants should consider:

* **Repository Stage** — Early prototype, maturing library, or release-ready.
* **User Intent** — Exploration, implementation, debugging, or release prep.
* **Scope of Change** — Suggest proportionate solutions; avoid overengineering.
* **Existing Patterns** — Align with established conventions before suggesting new ones.

---

## 🧱 Code Quality

* Clear, intentional naming and structure.
* Only meaningful comments remain (focus on *why* decisions were made).
* No unused variables, dead code, or debug leftovers.
* Logging is clean, consistent, and safe — no sensitive data.
* Public APIs are coherent and documented.
* Breaking changes are intentional and clearly communicated.

---

## ⚠️ Error Handling & Diagnostics

* Exceptions must be meaningful and actionable.
* Use specific exception types instead of generic ones.
* Fail early with clear validation messages.
* Logs and errors should guide users toward resolution.

---

## ⚡ Performance & Efficiency

* Avoid premature optimization, but eliminate obvious inefficiencies.
* Justify allocations in hot paths and document trade-offs.
* Use async patterns correctly (`Task`, `ValueTask`, no `async void`).
* Record major performance-related choices in ADRs or docs.

---

## 🎨 API Design (.NET)

* Follow .NET naming conventions (PascalCase, Async suffix, etc.).
* Cancellation tokens as the last parameter.
* Prefer interfaces/abstractions for inputs, concrete types for outputs.
* Avoid `out` parameters; prefer tuples or return objects.
* Enable nullable reference types and ensure annotations are correct.

### ✅ Good API Design

```csharp
// Async suffix, CancellationToken last, nullable annotations
public async Task<ConfigurationData?> LoadConfigAsync(string path, CancellationToken cancellationToken = default)
{
    // ...
}

// Interface input, concrete output, no out params
public ValidationResult Validate(IConfiguration config)
{
    return new ValidationResult(IsValid: true, Errors: Array.Empty<string>());
}
```

### ❌ Poor API Design

```csharp
// Missing Async suffix, CancellationToken not last
public async Task<ConfigurationData> LoadConfig(CancellationToken cancellationToken, string path)
{
    // ...
}

// Concrete input, out parameter instead of return value
public bool TryValidate(Configuration config, out List<string> errors)
{
    // ...
}
```

---

## 🧾 Documentation

* **README.md** provides concise overview and links deeper docs if needed.
* **CHANGELOG.md** lists meaningful changes since the last tag.
* **/docs/** contains advanced usage, architecture, and ADRs.
* All docs accurately reflect the current codebase.

---

## 📚 Dependencies

* Minimize external dependencies; every one adds risk.
* Prefer stable, widely used libraries over experimental ones.
* Document the reason for major dependencies.
* Keep dependencies updated but test thoroughly before upgrading.

---

## 📂 Local-only Working Files (`.local/`)

A repository-scoped **`.local/`** folder may exist and is **git-ignored**.

### ✅ Appropriate Uses

* Release preparation checklists and scratch notes
* Generated diff analyses or API inventories
* Draft ADRs or design explorations not yet ready for review
* Meeting notes or discussion artifacts
* Personal TODO lists or investigation notes
* Temporary test data or sample files

### ❌ Inappropriate Uses

* **Secrets or credentials** — Use OS keychain/secret manager instead
* **Build artifacts** — Use `bin/`, `obj/`, or dedicated build output directories
* **Shared documentation** — Belongs in `/docs/` under version control
* **Configuration templates** — Belongs in repo with `.example` suffix
* **Production data** — Never store real user data, even temporarily

### 🔒 Rules

* **Not authoritative**: Never reference `.local/` from README, docs, code, or CI. Do not assume it exists.
* **No release artifacts**: Nothing from `.local/` should ship in packages, images, or releases.
* **Security**: Avoid storing secrets in plaintext even here; prefer local secret stores/encrypted files. Never promote `.local/` content into the repo without review.
* **AI assistant behavior**: Treat `.local/` as ephemeral context only. Do not inline, quote, or depend on it when generating public docs or code comments.

---

## 🧪 Testing & Behavior

* Tests represent real-world behavior and critical paths.
* No broken, outdated, or redundant tests.
* Regression tests exist for fixed bugs.
* Test coverage trends should not regress significantly.

---

## 🔒 Security & Safety

* Never commit secrets or credentials.
* Sensitive data must not appear in logs or dumps.
* Dependencies checked for known vulnerabilities.
* External licenses compatible with this project’s license.
* Secrets handled securely and cleared when possible.

---

## 📦 Release Readiness

A release is **ready** when the following are true. These criteria are explicit so AI assistants base results on **real code changes** (not just the last commit message):

### A) Compare to the last tag (**actual code**, not commit subjects)

- [ ] Determine latest tag via `git describe --tags --abbrev=0`
- [ ] Analyze **code diff** from tag to HEAD (not just commit messages)
- [ ] Summarize meaningful changes in behavior, API, performance
- [ ] Identify and document breaking changes with impact assessment

### B) Changelog & Versioning (SemVer)

- [ ] Update **CHANGELOG.md** with entries **derived from actual code changes** since the previous tag (avoid relying on commit messages alone)
- [ ] Apply **Semantic Versioning** consistently:
  * **MAJOR** – breaking changes
  * **MINOR** – new features, backwards compatible
  * **PATCH** – fixes and small safe improvements
- [ ] Ensure the project/package version matches the intended release number

### C) Documentation in Sync

- [ ] **README.md** reflects current features, quickstart, and key examples
  * If README grows too large, recommend restructuring: keep pitch/install/quickstart in README; move advanced topics to **/docs/**
- [ ] Update or add docs for:
  * New/changed **public APIs**
  * **Configuration / environment variables**
  * **Migration notes** for breaking changes
- [ ] Ensure example code and snippets correspond to the current API

### D) Repository Hygiene

- [ ] Presence & freshness of:
  * `LICENSE`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`
  * `.github/` issue/PR templates and any workflow docs
  * Badges/links are correct and not broken
- [ ] Package metadata is accurate (description, license, repository URL, tags)

### E) Code Quality (static + structural)

- [ ] Remove unused code: variables, parameters, imports, dead files
- [ ] Public API is coherent and discoverable; obsolete members are marked and documented with migration guidance
- [ ] Logging: no secrets; appropriate log levels; no stray debug prints in release paths
- [ ] Error handling provides useful context and fails early for invalid inputs
- [ ] Hot paths avoid unnecessary allocations/copies; performance trade-offs are documented where relevant
- [ ] Comments adhere to the **why-not-what** policy

### F) CI/CD & Packaging

- [ ] All checks pass (build, analyzers/formatting, tests, security scans)
- [ ] Packaging configuration is correct (symbols/SourceLink if applicable) and produces reproducible artifacts
- [ ] Release process targets the intended platforms (including Windows ARM64 when applicable)

---

## 💬 AI Assistant Behavior

* Respect the **why-not-what** rule for comments.

* Prefer clearer naming/structure over extra docs.

* **When asked "Is this ready for release?"**: systematically verify all Release Readiness items **based on the diff to the last tag**, not on the latest commit or the current Unreleased notes.

* When suggesting refactoring, explain benefits, trade-offs, and potential risks.

* When generating tests, focus on meaningful behavior and edge cases over raw coverage.

* When reviewing PRs, highlight deviations from these principles and explain *why* they matter.

---

### 🔍 Release Verification Deep-Dive Methodology

When verifying release readiness, **systematically analyze the diff** rather than relying on memory or commit messages:

#### 1. Enumerate ALL new/changed public APIs

```powershell
# Find all public API changes since last tag
$lastTag = git describe --tags --abbrev=0
git diff $lastTag..HEAD -- '*.cs' | Select-String '^\+.*public'
```

- [ ] List each method/class/property explicitly
- [ ] Don't summarize or group—enumerate individually
- [ ] Note parameter types, return types, and access modifiers

#### 2. Cross-reference API names in documentation

- [ ] For each API in code, search docs for exact name match
- [ ] Flag discrepancies (e.g., `UseCertificatesFromFolder` in code vs `UseCertificateFromFolder` in docs)
- [ ] Verify parameter counts and types match examples
- [ ] Check that all parameters are documented

#### 3. Analyze each API for design consistency

- [ ] Async methods end with `Async` suffix
- [ ] CancellationToken is last parameter (if present)
- [ ] Nullable reference type annotations are accurate
- [ ] Naming follows .NET conventions (PascalCase, descriptive)
- [ ] XML documentation comments are present and accurate
- [ ] Return types appropriate (interface for input, concrete for output)

#### 4. Assess breaking change impact precisely

Don't assume—analyze each change systematically:

- [ ] **Identify** exact type/member that changed
- [ ] **Determine** who is affected:
  - All users (public API change)
  - Advanced scenarios (extensibility point)
  - Internal only (implementation detail)
- [ ] **Estimate** surface area:
  - **High**: Core API used in quickstart/common scenarios
  - **Medium**: Feature-specific API used in advanced scenarios
  - **Low**: Edge case or rarely-used functionality
- [ ] **Document** migration path for each breaking change in docs

#### 5. Validate all documentation examples

- [ ] Extract code snippets from README.md and /docs/
- [ ] Verify each snippet would compile against current code
- [ ] Check namespaces are correct and imported
- [ ] Verify method signatures match (names, parameter order, types)
- [ ] Confirm return types and patterns are accurate

#### 6. Confirm test coverage for new functionality

- [ ] Each new public API has corresponding tests
- [ ] Tests cover happy path scenarios
- [ ] Tests cover edge cases and boundary conditions
- [ ] Tests cover error conditions and exceptions
- [ ] Test names clearly describe what they verify

---

## ⚠️ Common Pitfalls to Avoid

* **Documentation lag** — Examples reference old API signatures or removed features
* **Inconsistent naming** — Plural vs singular (`UseCertificate` vs `UseCertificates`), inconsistent prefixes
* **Async suffix inconsistency** — Some async methods missing `Async` suffix
* **Breaking changes unmarked** — Changed signatures without major version bump or migration guide
* **Orphaned tests** — Tests for removed features still in test suite
* **Dead configuration** — Unused config keys or environment variables still documented
* **Stale badges** — Build status or version badges pointing to wrong branch/package
* **Hardcoded examples** — Code snippets with specific versions, paths, or outdated imports

---

## 🤔 When in Doubt

If uncertain about a decision, AI assistants should:

1. **Favor existing patterns** — Check how similar problems are solved elsewhere in the codebase before introducing new approaches
2. **Prefer standard library** — Use BCL types/patterns over custom implementations unless there's a documented reason
3. **Ask, don't assume** — Flag ambiguity and present options rather than guessing user intent
4. **Suggest, don't dictate** — Present alternatives with clear trade-offs (performance, maintainability, complexity)
5. **Cite this document** — Reference specific AGENTS.md sections when explaining recommendations

---

## 🌿 Version Control

* Commit messages describe *why*, not just *what*.
* Feature branches stay focused and short-lived.
* No force-pushes to main.
* Tags follow SemVer (`v1.2.3`).
* Breaking changes require major version and migration notes.

---

## ✅ Definition of Done

A change or release is complete when:

* Code and docs align with these principles.
* Changelog and version accurately describe reality.
* No unused or redundant elements remain.
* Intent is clear through naming, structure, or concise *why* comments.

---

**Version:** 1.0.0

> This file defines the authoritative AI guidance for this repository.
> Copilot, Claude, ChatGPT, and other assistants should treat this as the primary behavioral contract.
