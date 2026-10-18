# Releasing Avalonia.React

Avalonia.React publishes three NuGet packages and one npm package:

- `Avalonia.React`
- `Avalonia.React.Vite`
- `Avalonia.React.SourceGenerator`
- `@avalonia-react/bridge`

## Versioning

Update the version in all three places and keep them identical:

1. `Directory.Build.props` → `AvaloniaReactVersion`
2. `client/package.json` → `version`
3. `client/package-lock.json` → `version` (both the root and `packages.""` entries)

Add a matching section to `CHANGELOG.md`, then commit and push a tag named `vX.Y.Z`.

## Required secrets

Configure these repository secrets before publishing:

- `NUGET_API_KEY` — nuget.org API key with push permission for the three package IDs.
- `NPM_TOKEN` — npm automation token with publish permission for the `@avalonia-react` scope.

If a secret is missing, that registry is skipped; the GitHub release is still created.

## Release workflow

`.github/workflows/release.yml` runs when a `v*` tag is pushed. It:

1. Verifies the tag matches both `Directory.Build.props` and `client/package.json`.
2. Runs the control, source-generator, and bridge-client tests.
3. Builds the TypeScript client and verifies its publish manifest.
4. Packs the three NuGet packages and the npm tarball.
5. Verifies NuGet package contents with `eng/verify-nuget-packages.ps1`.
6. Publishes NuGet packages (including `.snupkg` symbols) and the npm package when their secrets are set.
7. Creates a GitHub release and attaches the `.nupkg`, `.snupkg`, and `.tgz` files.

## Local verification

Run the same checks before tagging:

```sh
dotnet build Avalonia.React.slnx -c Release -p:AvaloniaReactBuildOnBuild=false -p:AvaloniaReactAutoStart=false
dotnet test tests/Avalonia.React.Tests/Avalonia.React.Tests.csproj -c Release
dotnet test tests/Avalonia.React.SourceGenerator.Tests/Avalonia.React.SourceGenerator.Tests.csproj -c Release

npm --prefix client ci
npm --prefix client run build
npm --prefix client test
npm --prefix client run verify:package
# Package the tarball (npm pack does not honor --prefix for its destination):
cd client && npm pack --pack-destination ../artifacts/packages && cd ..

dotnet pack src/Avalonia.React/Avalonia.React.csproj -c Release -o artifacts/packages
dotnet pack src/Avalonia.React.Vite/Avalonia.React.Vite.csproj -c Release -o artifacts/packages
dotnet pack src/Avalonia.React.SourceGenerator/Avalonia.React.SourceGenerator.csproj -c Release -o artifacts/packages
pwsh -NoProfile -ExecutionPolicy Bypass -File eng/verify-nuget-packages.ps1
```

`npm pack` prints the tarball contents; `verify:package` also fails if source tests or `node_modules` leak into the package. The NuGet check verifies required targets/analyzer/readme/license entries and rejects `src`, `tests`, `bin`, and `obj` content.

## Package contents

- `Avalonia.React` — `lib/net10.0`, build-transitive targets, README, LICENSE.
- `Avalonia.React.Vite` — build-transitive targets only, README, LICENSE.
- `Avalonia.React.SourceGenerator` — Roslyn analyzer, build-transitive props, README, LICENSE.
- `@avalonia-react/bridge` — ESM `dist/index.js`, `dist/index.d.ts`, README, LICENSE; no source, tests, or dependencies.

Publishing the `@avalonia-react` scope requires that the npm organization already exists and the token has access. The package is ESM-only and requires Node 18 or a compatible bundler.
