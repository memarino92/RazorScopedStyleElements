# RazorScopedStyleElements

**Single-file Razor components with native scoped CSS.**

Write scoped CSS directly inside `<style>` elements in `.razor` components. RazorScopedStyleElements extracts the styles at build time and feeds them into Blazor's existing CSS isolation pipeline.

[![NuGet](https://img.shields.io/nuget/v/RazorScopedStyleElements.svg)](https://www.nuget.org/packages/RazorScopedStyleElements)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

```razor
<p>This text is purple</p>

<style>
    /* Everything in here is scoped to this component */
    p {
        color: rebeccapurple;
    }
</style>
```

- **No runtime dependency**: this is build-time tooling, and the application receives no RazorScopedStyleElements runtime assembly.
- **Native CSS isolation**: scope generation, selector rewriting, bundling, static web assets, and publishing remain owned by the Razor SDK.
- **No generated source files in the repository**: transformed Razor and extracted CSS files are intermediate artifacts under `obj`.

RazorScopedStyleElements is an authoring layer over the existing Razor CSS isolation pipeline. It does not implement CSS scoping itself.

## Why?

Blazor CSS isolation conventionally keeps component markup and scoped styles in sibling files:

```text
Components/
|-- Card.razor
`-- Card.razor.css
```

RazorScopedStyleElements offers an alternative that keeps the scoped styles in the component:

```text
Components/
`-- Card.razor
```

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

Component markup, logic, and scoped styles can remain colocated when that authoring model is preferable. Conventional sibling `.razor.css` files remain available for components where file separation is a better fit.

## Installation

```shell
dotnet add package RazorScopedStyleElements
```

The NuGet package is the entire setup. No separate preprocessing command, tool manifest, or runtime dependency is required. Continue using normal .NET CLI workflows:

```shell
dotnet build
dotnet watch
dotnet test
dotnet publish
```

### Requirements

- .NET 10 SDK / MSBuild 18 or later
- An SDK-style Razor or Blazor project with scoped CSS enabled

The package uses NuGet `build/` assets and is imported by a direct `PackageReference`.

## Usage

Version 0.1 supports one static, top-level `<style>` element without attributes in a `.razor` component. The element may appear before, between, or after top-level component markup, including before or after an `@code` block. Empty style elements are accepted.

### Supported Syntax

- Normal static CSS, including comments, strings, and braces
- Pseudo-selectors
- CSS at-rules such as `@media` and `@supports`
- `::deep` and other syntax supported by Blazor CSS isolation
- One empty or non-empty top-level `<style>` element per component

The CSS is preserved for the Razor SDK to process. Components without a supported style element are left untouched. Inline CSS and conventional sibling `.razor.css` components can coexist in the same project.

### Unsupported Syntax

- Multiple `<style>` elements in one component
- Nested or conditionally rendered `<style>` elements
- Attributes on the `<style>` element
- Runtime Razor expressions inside CSS
- Inline CSS combined with a sibling `.razor.css` file for the same component

Unsupported forms produce build diagnostics rather than being interpreted heuristically.

## Diagnostics

| Code | Meaning |
| --- | --- |
| `RSSE001` | A component contains more than one inline style element. |
| `RSSE002` | A style element is nested in markup or conditional Razor code instead of being top-level. |
| `RSSE003` | A style element uses attributes, dynamic Razor content, or malformed or unsupported syntax. |
| `RSSE004` | A component has both inline CSS and a physical sibling `.razor.css` file. |

Diagnostics identify the original `.razor` source and include line and column information where available.

## Configuration

Disable all transformation for a project with the package kill switch:

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

## How It Works

RazorScopedStyleElements hooks into the Razor SDK through MSBuild, transforms the Razor compilation input, and registers the extracted stylesheet as a scoped CSS input.

```text
Component.razor
      |
      v
<style> extraction
      |
      |-- transformed Component.razor
      |
      `-- generated Component.razor.css
                        |
                        v
              Razor CSS isolation
                        |
                        v
                 Scoped CSS bundle
```

The NuGet target runs immediately before the Razor SDK's `ComputeCssScope` target after Razor component inputs are resolved. For each participating component it:

1. Structurally scans Razor and HTML while tracking element depth and skipping Razor code, strings, and comments.
2. Identifies one supported top-level `<style>` element.
3. Writes a transformed Razor input and extracted `.razor.css` input under the intermediate `obj` directory without modifying the source component or rewriting unchanged generated files.
4. Replaces the physical `RazorComponent` input while preserving its logical `TargetPath`, then registers the generated CSS as `ScopedCssInput` with matching `RazorComponent` metadata.
5. Leaves scope generation, selector rewriting, bundling, static web asset handling, and publishing behavior to the Razor SDK and Blazor CSS isolation.

The extractor deliberately uses a bounded structural tokenizer instead of regex and diagnoses unsupported ambiguity. It does not reimplement Razor compilation or CSS isolation.

RazorScopedStyleElements is an authoring layer over the existing Razor CSS isolation pipeline. It does not implement CSS scoping itself.

## Prior Art and Motivation

Razor already allows component logic to be colocated with markup through `@code`, while CSS isolation conventionally uses a sibling `.razor.css` file. Colocated component styling is also familiar from the component models used by [Svelte](https://svelte.dev/docs/svelte/svelte-files) and [Vue](https://vuejs.org/guide/scaling-up/sfc.html).

There is an open [Razor language-design discussion](https://github.com/dotnet/razor/issues/10766) about a first-class `@style` directive for colocated styles. RazorScopedStyleElements explores a similar authoring model today while continuing to delegate CSS isolation to the existing Razor SDK; it is not an official implementation of that proposal.

## Limitations

Version 0.x deliberately supports a constrained authoring model:

- Only direct package references are imported; `buildTransitive/` behavior is not enabled.
- Style attributes and multiple, nested, or dynamically conditional style elements are unsupported.
- Razor expressions cannot supply CSS values at runtime.
- A component cannot combine inline CSS with its own sibling `.razor.css` file.
- No editor extension, CSS language-service integration, syntax highlighting, or completion is provided for CSS inside the `<style>` element.

## Development

```shell
dotnet build RazorScopedStyleElements.slnx
dotnet test RazorScopedStyleElements.slnx
dotnet pack src/RazorScopedStyleElements.Package/RazorScopedStyleElements.Package.csproj
```

The integration tests pack the package, install it from an isolated local NuGet feed into fresh SDK projects, and verify Razor SDK-generated isolated CSS across build and publish scenarios.

See [CONTRIBUTING.md](CONTRIBUTING.md) for development workflow, architecture constraints, and pull request guidance. Please report security issues according to [SECURITY.md](SECURITY.md).

## License

Licensed under the [MIT License](LICENSE.txt).
