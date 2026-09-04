# RazorScopedStyleElements

**Write scoped CSS directly inside your Razor components.**

[![NuGet](https://img.shields.io/nuget/v/RazorScopedStyleElements.svg)](https://www.nuget.org/packages/RazorScopedStyleElements)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

```razor
<div class="card">
    Hello
</div>

<style>
    .card {
        padding: 1rem;
    }
</style>
```

RazorScopedStyleElements extracts the `<style>` block at build time and passes it to Blazor's native CSS isolation pipeline.

- No runtime dependency.
- No custom scoping implementation.
- No generated files in your repository.

> **Architecture:** RazorScopedStyleElements is an authoring layer over the existing Razor CSS isolation pipeline; it does not implement CSS scoping itself.

## Install

```shell
dotnet add package RazorScopedStyleElements
```

That is almost the entire setup. Continue using normal .NET commands:

```shell
dotnet build
dotnet test
dotnet publish
dotnet watch
```

The package removes the style element from the Razor compilation input, extracts its contents under `obj`, and hands the generated `.razor.css` input to Microsoft's existing isolation pipeline. Source files are never modified, and the application receives no RazorScopedStyleElements runtime assembly.

## Requirements

- .NET 10 SDK / MSBuild 18 or later
- An SDK-style Razor or Blazor project with scoped CSS enabled

The package uses NuGet `build/` assets and is imported by a direct `PackageReference`. No additional command, tool manifest, or runtime dependency is required.

## Supported Syntax

Version 0.1 supports exactly one static, top-level `<style>` element without attributes per component. It may appear before, between, or after top-level component markup. Empty styles are accepted.

CSS comments, strings, braces, pseudo-selectors, CSS at-rules such as `@media` and `@supports`, and Razor CSS isolation syntax such as `::deep` are preserved. Runtime Razor expressions inside CSS are not supported.

Components without a supported style element are left untouched. Inline CSS and conventional sibling `.razor.css` components can coexist in the same project, but a single component cannot use both forms.

## Configuration

Disable all transformation for a project:

```xml
<PropertyGroup>
  <RazorScopedStyleElementsEnabled>false</RazorScopedStyleElementsEnabled>
</PropertyGroup>
```

Override the generated-file root when necessary:

```xml
<PropertyGroup>
  <RazorScopedStyleElementsIntermediateOutputPath>$(IntermediateOutputPath)custom-scoped-styles/</RazorScopedStyleElementsIntermediateOutputPath>
</PropertyGroup>
```

Custom paths should remain beneath `IntermediateOutputPath` and must remain unique per target framework.

## Diagnostics

| ID | Description |
| --- | --- |
| `RICSS001` | A component contains more than one inline style element. |
| `RICSS002` | A style element is nested in markup or conditional Razor code instead of being top-level. |
| `RICSS003` | A style element uses attributes, dynamic Razor content, or malformed/unsupported syntax. |
| `RICSS004` | A component has both inline CSS and a physical sibling `.razor.css` file. |

Diagnostics identify the original `.razor` source and include line and column information where available.

## How It Works

The NuGet target runs immediately before the SDK's `ComputeCssScope` target after resolving Razor component inputs. For each supported component it:

1. Structurally scans Razor/HTML while tracking element depth and skipping Razor code, strings, and comments.
2. Writes a transformed Razor input and extracted `.razor.css` input under `obj` without rewriting unchanged files.
3. Replaces the physical `RazorComponent` while preserving its original logical `TargetPath`.
4. Registers the CSS as `ScopedCssInput` with matching `RazorComponent` metadata.
5. Leaves all isolation processing to the Microsoft SDK.

The extractor deliberately uses a bounded structural tokenizer instead of regex and diagnoses unsupported ambiguity. It does not reimplement Razor compilation or CSS isolation.

## Limitations

- Only direct package references are imported; `buildTransitive/` behavior is not enabled.
- Style attributes and multiple, nested, or dynamically conditional style elements are unsupported.
- Razor expressions cannot supply CSS values at runtime.
- A component cannot combine inline CSS with its own sibling `.razor.css` file.
- Editor syntax highlighting and completion are outside this build-time package.

## Development

```shell
dotnet build RazorScopedStyleElements.slnx
dotnet test RazorScopedStyleElements.slnx
dotnet pack src/RazorScopedStyleElements.Package/RazorScopedStyleElements.Package.csproj
```

The integration tests pack the package, install it from an isolated local NuGet feed into fresh SDK projects, and verify Microsoft-generated isolated CSS across build and publish scenarios.

See [CONTRIBUTING.md](CONTRIBUTING.md) for development workflow, architecture constraints, and pull request guidance. Please report security issues according to [SECURITY.md](SECURITY.md).

## License

Licensed under the [MIT License](LICENSE.txt).
