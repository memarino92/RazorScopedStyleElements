# Contributing to RazorScopedStyleElements

Contributions are welcome through GitHub issues and pull requests. Keep proposals focused on portable build-time behavior and integration with the Microsoft Razor SDK.

## Prerequisites

- .NET SDK 10.0.102 or a compatible 10.0 feature-band SDK selected by `global.json`
- Git

## Build and Test

```shell
dotnet restore RazorScopedStyleElements.slnx
dotnet format RazorScopedStyleElements.slnx --no-restore --verify-no-changes
dotnet build RazorScopedStyleElements.slnx --configuration Release --no-restore
dotnet test RazorScopedStyleElements.slnx --configuration Release --no-build
```

Create a local package with:

```shell
dotnet pack src/RazorScopedStyleElements.Package/RazorScopedStyleElements.Package.csproj --configuration Release --output artifacts/packages
```

Use a unique prerelease version when repeatedly testing local packages because NuGet treats an installed package version as immutable:

```shell
dotnet pack src/RazorScopedStyleElements.Package/RazorScopedStyleElements.Package.csproj \
  --configuration Release \
  --output artifacts/packages \
  -p:PackageVersion=0.1.1-local.1
```

## Architecture Guardrails

- Delegate CSS scoping, selector rewriting, bundling, static web assets, RCL behavior, and publishing to the Microsoft Razor SDK.
- Do not add a runtime Blazor dependency or runtime assembly to consuming applications.
- Never modify source `.razor` files or write generated files outside the intermediate output tree.
- Preserve the original logical component path when replacing a physical Razor compilation input.
- Avoid regex-based Razor parsing. Unsupported syntax should produce a stable `RSSExxx` diagnostic rather than a heuristic transformation.
- Avoid rewriting unchanged generated files; incremental build and `dotnet watch` behavior are part of the product contract.
- Prefer end-to-end SDK integration tests over mocked MSBuild behavior.

## Pull Requests

1. Open an issue first for substantial behavior or architecture changes.
2. Add focused unit tests for extraction behavior and integration tests for MSBuild or SDK behavior.
3. Run formatting, build, and all tests locally.
4. Use a Conventional Commit-style title such as `feat:`, `fix:`, `test:`, `docs:`, or `build:`.
5. Explain user-visible behavior, compatibility implications, and any SDK assumptions in the pull request.

CI runs the complete suite on Ubuntu, Windows, and macOS. Pull requests should not be merged while any platform is failing.

## Reporting Bugs

Include the .NET SDK version, operating system, project SDK, minimal `.razor` input, project properties, full build diagnostic, and a reproducible sample when practical. A binary log can help diagnose MSBuild ordering, but check it for secrets and machine-specific data before sharing it.

## Maintainer Releases

Publishing uses NuGet trusted publishing through GitHub OIDC and does not require a long-lived API key.

One-time repository and NuGet.org setup:

1. Create a GitHub environment named `nuget.org`. Add required reviewers if release approval is desired.
2. Add a repository Actions variable named `NUGET_USER` containing the NuGet.org account username that owns the package.
3. In the NuGet.org account, create a trusted publishing policy for this GitHub owner and repository.
4. Set the policy workflow file to `publish.yml` and environment to `nuget.org`.
5. Ensure the policy permits package ID `RazorScopedStyleElements`.

Create and push a version tag only after CI passes on the release commit:

```shell
git tag v0.1.0
git push origin v0.1.0
```

The tag value without the leading `v` becomes the NuGet package version. The publish workflow rebuilds, retests, packs, authenticates with `NuGet/login@v1`, and pushes the package to NuGet.org.

## Conduct

Be respectful, constructive, and specific. Harassment, personal attacks, and discriminatory behavior are not acceptable in project spaces.
