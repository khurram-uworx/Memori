# Release Guide

This document describes the versioning policy, release workflow, and NuGet publish procedure for the Memori library.

## Versioning Policy

Memori follows [SemVer 2.0](https://semver.org/).

- **Major**: breaking changes to public API (`IConversationStorage`, `Memori`, `MemoriOptions`, `IAugmentationClient`, `IEmbeddingGenerator`, `IChatClient` middleware contracts, DI registration surface).
- **Minor**: new features, new optional parameters, new extension points (fully backward-compatible).
- **Patch**: bug fixes, internal refactors, test additions (no public API change).

Until 1.0.0, minor versions may include breaking changes if clearly documented in release notes. The major version will be bumped to 1.0.0 when the API surface is stable.

Pre-release suffixes (`-alpha`, `-beta`, `-rc.1`) may be used for early access releases.

## Branch Strategy

- `main` — stable, release-ready.
- Feature branches merge to `main` via PR.
- Release tags are applied directly on `main`.

## Prerequisites

- .NET 10 SDK or newer.
- NuGet API key (set as `NUGET_API_KEY` environment variable).

## Build and Verify

```bash
# Restore
dotnet restore

# Build
dotnet build --configuration Release

# Run all tests
dotnet test --configuration Release

# Pack
dotnet pack src/Memori/Memori.csproj --configuration Release -o ./artifacts
```

The `.nupkg` file is written to `./artifacts/Memori.<version>.nupkg`.

## CI Validation

The CI workflow (`.github/workflows/ci.yml`) runs on every push/PR to `main`:

1. Restore
2. Build (Release)
3. Test with code coverage
4. Pack (validates package metadata)

All four steps must pass before merging.

## NuGet Publish

### Manual publish (from local)

```bash
# Build and pack
dotnet pack src/Memori/Memori.csproj --configuration Release -o ./artifacts

# Publish
dotnet nuget push ./artifacts/Memori.<version>.nupkg \
    --source https://api.nuget.org/v3/index.json \
    --api-key $NUGET_API_KEY
```

### GitHub Actions publish

To publish from CI, add the following to `.github/workflows/ci.yml`:

```yaml
- name: Publish to NuGet
  if: startsWith(github.ref, 'refs/tags/v')
  run: |
    dotnet pack src/Memori/Memori.csproj --configuration Release -o ./artifacts
    dotnet nuget push ./artifacts/Memori.*.nupkg \
      --source https://api.nuget.org/v3/index.json \
      --api-key ${{ secrets.NUGET_API_KEY }}
```

Trigger by pushing a tag matching `v*`:

```bash
git tag v0.2.0
git push origin v0.2.0
```

## Release Checklist

1. [ ] All tests pass (`dotnet test --configuration Release`).
2. [ ] `CHANGELOG.md` is up to date for the new version.
3. [ ] Version in `src/Memori/Memori.csproj` matches the intended release.
4. [ ] Package metadata is complete (readme, license, tags, release notes).
5. [ ] Build is green in CI.
6. [ ] Tag is created and pushed: `git tag v<version> && git push origin v<version>`.
7. [ ] NuGet package is published.
8. [ ] Release notes are posted on GitHub Releases.

## Package Contents

The `Memori` NuGet package ships:

- `net10.0` target.
- XML documentation file (enables IDE intellisense).
- SourceLink-enabled debug symbols (embedded in the `.snupkg`).

## Supported Target

- `net10.0` — .NET 10 or newer.
