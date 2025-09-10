# Contributing

Thanks for your interest in contributing to Cocoar.Configuration!

## Getting started
- Fork the repository and create a feature branch.
- Ensure you have the .NET SDK specified in `global.json`.
- Run the test suite locally before opening a PR.

## Coding guidelines
- Prefer small, focused PRs.
- Keep public APIs stable; consider extension methods for new fluent entries.
- Add or update tests for all behavior changes.
- Keep provider options deterministic (stable keys) to preserve instance pooling.

## Testing
- Run unit tests under `src/tests/Cocoar.Configuration.Tests`.
- For change-driven providers (file/HTTP), prefer deterministic waits (poll loops with upper bounds) to avoid flakiness.

## Commit/PR
- Reference related issues in the PR description.
- Describe user-facing changes and migration notes if any.

## License
By contributing, you agree that your contributions will be licensed under the MIT License.