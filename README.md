# RazorInlineCss

RazorInlineCss is a build-time extension for authoring component-scoped CSS in a top-level `<style>` element inside a `.razor` file. It delegates selector rewriting, scope generation, bundling, static web assets, Razor Class Library behavior, and publishing to the Microsoft Razor SDK CSS isolation pipeline.

The project is under initial development. Package usage and supported syntax will be documented before the first release.

Licensed under the [MIT License](LICENSE).

## Repository layout

- `src/RazorInlineCss.Tasks` contains the MSBuild task implementation.
- `src/RazorInlineCss.Package` produces the NuGet package and its MSBuild targets.
- `tests/RazorInlineCss.Tasks.Tests` contains parser and transformation unit tests.
- `tests/RazorInlineCss.IntegrationTests` exercises fresh SDK-style projects through the `dotnet` CLI.

## Build

```shell
dotnet build
dotnet test
```
