# `dotnet watch` Hot Reload Research

Research date: September 7, 2026

Related repository work:

- [Issue #8: `dotnet watch` Hot Reload Changes Not Applied](https://github.com/memarino92/RazorScopedStyleElements/issues/8)
- [PR #9: document and test restart mode](https://github.com/memarino92/RazorScopedStyleElements/pull/9)
- [Razor proposal #10766: `@style` directive similar to scoped CSS files](https://github.com/dotnet/razor/issues/10766)

## Conclusion

True, state-preserving `dotnet watch` Hot Reload cannot currently be implemented transparently by RazorScopedStyleElements through supported package-level .NET 10 or .NET 11 extension points.

The package changes the physical Razor compilation input in an MSBuild target. Hot Reload observes the authored `.razor` file, but the Razor workspace consumes a transformed `.razor` file under `obj`. An ordinary Hot Reload edit updates only a Roslyn document with the same physical path. It does not rerun arbitrary MSBuild targets or know that the authored file must regenerate another compiler input.

The recommended direction is:

1. Retain `dotnet watch --no-hot-reload` as the supported workaround. It performs a build and application restart, so the transformation runs.
2. Do not write files from a source generator and do not launch a persistent watcher from an MSBuild task.
3. Request a generic `dotnet/sdk` watch capability that lets a watched input trigger design-time project reevaluation and generated-input refresh before Hot Reload computes deltas.
4. Participate in the first-class Razor inline scoped-style proposal as the best long-term solution.
5. Consider a foreground wrapper tool only if CLI-only Hot Reload justifies the additional complexity.

## Current Pipeline

[`TransformRazorComponents.cs`](src/RazorScopedStyleElements.Tasks/TransformRazorComponents.cs) reads each authored component and writes:

- A transformed `.razor` file under `obj/.../RazorScopedStyleElements/razor`.
- An extracted `.razor.css` file under `obj/.../RazorScopedStyleElements/css`.

[`RazorScopedStyleElements.targets`](src/RazorScopedStyleElements.Package/build/RazorScopedStyleElements.targets) runs the task before `ComputeCssScope`, removes participating authored files from `RazorComponent`, adds their transformed files with the original logical `TargetPath`, and adds the generated stylesheets to `ScopedCssInput`.

This is a supported and effective normal-build integration. It preserves source files, keeps generated files under the intermediate output tree, and delegates scope generation, selector rewriting, bundling, and static web assets to the Razor SDK.

It does not establish a persistent dependency in the Hot Reload workspace between:

```text
authored Component.razor
        |
        | MSBuild target execution only
        v
generated Component.razor + Component.razor.css
```

## Root Cause

### Verified behavior

Released .NET 10 performs an initial build/design-time evaluation and collects `Compile`, `AdditionalFiles`, and `Watch` inputs for an in-process Roslyn workspace:

- [Evaluation and file collection](https://github.com/dotnet/sdk/blob/e6bc966cc3d1348265b0831c6daca23267169d8f/src/BuiltInTools/dotnet-watch/Build/EvaluationResult.cs#L38-L162)
- [Workspace document updates](https://github.com/dotnet/sdk/blob/e6bc966cc3d1348265b0831c6daca23267169d8f/src/BuiltInTools/dotnet-watch/HotReload/IncrementalMSBuildWorkspace.cs#L119-L169)

For an ordinary changed file, the watcher updates the workspace document, additional document, or analyzer-config document with that same physical path:

- [Ordinary update handling](https://github.com/dotnet/sdk/blob/e6bc966cc3d1348265b0831c6daca23267169d8f/src/BuiltInTools/dotnet-watch/HotReload/HotReloadDotNetWatcher.cs#L491-L495)

The SDK explicitly describes the relevant generated-input architecture as unsupported:

- [.NET 10 `AcceptChange`](https://github.com/dotnet/sdk/blob/e6bc966cc3d1348265b0831c6daca23267169d8f/src/BuiltInTools/dotnet-watch/HotReload/HotReloadDotNetWatcher.cs#L646-L678)
- [.NET 11 Preview 7 equivalent](https://github.com/dotnet/sdk/blob/fb1d29c57648985a9ebbb4e2645a77bcc7e60e3f/src/Dotnet.Watch/Watch/HotReload/HotReloadDotNetWatcher.cs#L792-L801)
- [Current SDK `main`](https://github.com/dotnet/sdk/blob/fcd632a06320edcb05ac62f20e150da48b00b6a8/src/Dotnet.Watch/Watch/HotReload/HotReloadDotNetWatcher.cs#L807-L816)

The comment calls out an MSBuild target that adds generated source files under the intermediate output directory based on another input. It contrasts this with an `AdditionalFile` directly consumed by a Roslyn source generator, which can rerun in the active workspace.

This explains the observed sequence:

1. The authored `.razor` edit is detected.
2. The transformation target is not rerun.
3. The transformed Razor and generated CSS files do not change.
4. The authored path does not match the transformed Razor `AdditionalFile` known to the workspace.
5. The watcher reports `No C# changes to apply`.

### Watch item metadata

Adding generated files to `Watch` only makes those generated paths observable. It does not regenerate them when the authored path changes.

The older file-set representation preserves paths, containing projects, and static web asset paths, but not arbitrary metadata:

- [.NET 10 `FileSetSerializer`](https://github.com/dotnet/sdk/blob/e6bc966cc3d1348265b0831c6daca23267169d8f/src/BuiltInTools/DotNetWatchTasks/FileSetSerializer.cs#L12-L77)
- [.NET 10 `MSBuildFileSetResult`](https://github.com/dotnet/sdk/blob/e6bc966cc3d1348265b0831c6daca23267169d8f/src/BuiltInTools/dotnet-watch/Watch/MSBuildFileSetResult.cs#L8-L43)

The newer in-process path also flattens watched items into a model with no target, dependency, rebuild, or reevaluation metadata:

- [Current `FileItem`](https://github.com/dotnet/sdk/blob/fcd632a06320edcb05ac62f20e150da48b00b6a8/src/Dotnet.Watch/Watch/Build/FileItem.cs#L6-L18)

`CustomCollectWatchItems` adds target names to the design-time evaluation target set. It is not a callback invoked for each file change:

- [Current target collection](https://github.com/dotnet/sdk/blob/fcd632a06320edcb05ac62f20e150da48b00b6a8/src/Dotnet.Watch/Watch/Build/EvaluationResult.cs#L237-L271)

## Approach Comparison

| Approach | True Hot Reload | API status | Scoped CSS | IDE impact | Recommendation |
| --- | --- | --- | --- | --- | --- |
| Existing MSBuild transform | No | Supported | Supported | Build and IDE inputs can differ | Keep for normal builds |
| Add generated files to `Watch` | No by itself | Supported | No regeneration | No improvement | Insufficient |
| Roslyn analyzer or generator bridge | No | Public APIs are insufficient | Cannot emit `ScopedCssInput` | Cannot replace Razor input | Reject |
| Source generator writing files | Unreliable | Unsupported side effect | Racy and too late | Compiler-host side effects | Reject |
| Razor project-engine extension | Not injectable into SDK Razor | Mixed public/internal APIs | No static asset output | Would require a custom host | Reject for this package |
| Long-lived MSBuild watcher | Potentially, but unsafe | No suitable session lifecycle | Event and build races | Duplicate design-time watchers | Reject |
| Foreground wrapper tool | Potentially CLI-only | Public process and file APIs | Possible | Does not cover Visual Studio | Optional fallback |
| Upstream reevaluate-on-change support | Yes in principle | Requires new SDK API | Reuses SDK pipeline | Could refresh the project coherently | Preferred generic fix |
| First-class Razor inline style support | Yes | Requires a Razor feature | Native integration | Best diagnostics and mapping | Best long-term fix |

## Razor Source Generator

### Roslyn boundary

Roslyn exposes Razor files to generators as immutable `AdditionalText` inputs:

- [`AdditionalText`](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/src/Compilers/Core/Portable/DiagnosticAnalyzer/AdditionalText.cs#L10-L25)

Generators are additive and ordinarily unordered. They cannot mutate existing inputs or make one generator consume another generator's non-C# output:

- [Incremental generator model](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/docs/features/incremental-generators.cookbook.md#L41-L49)
- [Generator ordering request](https://github.com/dotnet/roslyn/issues/57239)

`GeneratorDriver.ReplaceAdditionalText` is public, but it is a compiler-host operation. An analyzer or generator does not receive the active driver and cannot use it to replace its own or Razor's inputs:

- [`GeneratorDriver.ReplaceAdditionalText`](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/src/Compilers/Core/Portable/SourceGeneration/GeneratorDriver.cs#L146-L179)

The Razor source generator selects `.razor` and `.cshtml` files directly from `AdditionalTextsProvider`, wraps the exact input, and reads its text:

- [.NET 10 `RazorSourceGenerator`](https://github.com/dotnet/razor/blob/5ad8bd68fb595a2a64e2db46023ad91c8e5f55ae/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/SourceGenerators/RazorSourceGenerator.cs#L39-L69)
- [.NET 10 `SourceGeneratorProjectItem`](https://github.com/dotnet/razor/blob/5ad8bd68fb595a2a64e2db46023ad91c8e5f55ae/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/SourceGenerators/SourceGeneratorProjectItem.cs#L12-L59)

It constructs a private virtual file system and project engine with built-in features. No public package registration mechanism was found for replacing documents or adding project-engine features to this generator.

### .NET 11 generator changes

.NET 11 introduces experimental `RegisterPreCompilationSourceOutput`, and newer Razor builds use it to emit component declaration C# before tag helper discovery:

- [Roslyn API source](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/src/Compilers/Core/Portable/SourceGeneration/IncrementalContexts.cs)
- [Tracking issue](https://github.com/dotnet/roslyn/issues/83089)

The API remains experimental as `RSEXPERIMENTAL007`. More importantly, it emits language source through `AddSource`. It cannot:

- Replace or add a Razor `AdditionalText`.
- Emit a CSS build artifact.
- Add or modify an MSBuild item.
- Establish a preprocessing order before the Razor generator reads its input.

Experimental generator host outputs likewise require a compiler host that understands the returned object. The SDK does not translate arbitrary host output into Razor documents or scoped CSS inputs.

Therefore .NET 11's generator staging does not provide a bridge for this package.

## Razor Project Engine Extensibility

Razor has public CLR APIs for constructing and customizing a standalone engine:

- [`RazorProjectEngine`](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/RazorProjectEngine.cs)
- [`RazorProjectEngineBuilder`](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/RazorProjectEngineBuilder.cs)
- [`IRazorOptimizationPass`](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/IRazorOptimizationPass.cs)
- [`IRazorDocumentClassifierPass`](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/IRazorDocumentClassifierPass.cs)
- [`RazorProjectFileSystem`](https://github.com/dotnet/roslyn/blob/0e119d1bc17697c7f8d9a8e142213c8557310c80/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/RazorProjectFileSystem.cs)

These APIs support a host that constructs and runs its own Razor engine. They do not provide package injection into the Razor SDK source generator.

An intermediate-node pass would also solve only part of the problem. It could potentially remove rendered markup after Razor parsing, but there is no corresponding pass output for an MSBuild static asset, and no public mapping layer for:

```text
generated C# -> transformed Razor -> authored Razor
```

Replacing or wrapping the SDK Razor compiler through internal types would be version-sensitive and likely conflict with Razor IDE tooling.

## Scoped CSS Boundary

Scoped CSS belongs to the MSBuild static-web-assets pipeline:

- [Scoped CSS input resolution](https://github.com/dotnet/sdk/blob/236ef332c38d1e89688189fe028d85c9dcb37761/src/StaticWebAssetsSdk/Targets/Microsoft.NET.Sdk.StaticWebAssets.ScopedCss.targets#L116-L143)
- [`RazorComponent` association](https://github.com/dotnet/sdk/blob/fcd632a06320edcb05ac62f20e150da48b00b6a8/src/RazorSdk/Targets/Sdk.Razor.CurrentVersion.targets#L690-L708)
- [`ApplyCssScopes`](https://github.com/dotnet/sdk/blob/fcd632a06320edcb05ac62f20e150da48b00b6a8/src/StaticWebAssetsSdk/Tasks/ScopedCss/ApplyCssScopes.cs#L112-L149)

The SDK supports a package adding a physical `ScopedCssInput` with `RazorComponent` metadata before the relevant targets run. The current package correctly uses that extension.

Roslyn generators cannot add MSBuild items or supported arbitrary non-source build outputs. General non-source generator output remains an open request:

- [Roslyn #57608](https://github.com/dotnet/roslyn/issues/57608)

A generator writing a CSS file directly would be an untracked side effect and would occur after MSBuild prepared the current invocation's scoped CSS inputs.

## .NET 11 SDK Assessment

The following SDK snapshots were checked through pinned upstream source:

| Snapshot | Commit | Result |
| --- | --- | --- |
| .NET 11 Preview 7, SDK `11.0.100-preview.7.26381.103` | [`fb1d29c`](https://github.com/dotnet/sdk/tree/fb1d29c57648985a9ebbb4e2645a77bcc7e60e3f) | Unsupported |
| `release/11.0.1xx`, September 4, 2026 | [`31118cf`](https://github.com/dotnet/sdk/tree/31118cfde1f4d217094cf199e1ec5885d2066311) | Unsupported |
| SDK `main`, September 4, 2026 | [`fcd632a`](https://github.com/dotnet/sdk/tree/fcd632a06320edcb05ac62f20e150da48b00b6a8) | Unsupported |

.NET 11 refactors `dotnet watch` around in-process project graph evaluation and reusable internal services:

- [ProjectGraph migration PR #49589](https://github.com/dotnet/sdk/pull/49589)
- [Watch implementation extraction PR #51223](https://github.com/dotnet/sdk/pull/51223)
- [Static web assets and scoped CSS PR #52225](https://github.com/dotnet/sdk/pull/52225)

These changes make an upstream implementation more practical, but do not expose a public plugin contract. The build manager, file handlers, workspace, and update providers remain internal:

- [`ProjectBuildManager`](https://github.com/dotnet/sdk/blob/fcd632a06320edcb05ac62f20e150da48b00b6a8/src/Dotnet.Watch/Watch/Build/ProjectBuildManager.cs)
- [Selected friend assemblies](https://github.com/dotnet/sdk/blob/fcd632a06320edcb05ac62f20e150da48b00b6a8/src/Dotnet.Watch/Watch/Properties/AssemblyInfo.cs#L6-L11)

Changes to known project/import files and some file additions can trigger reevaluation. An edit to an existing watched `.razor` file cannot opt into reevaluation, target execution, or original-to-generated path translation.

Newer SDK `main` also no longer sets `DotNetWatchBuild=true` during the Hot Reload design-time build:

- [PR #55861](https://github.com/dotnet/sdk/pull/55861)

Future package behavior should not depend on that property being set in Hot Reload mode.

## Rejected Designs

### Source generator file writes

Writing transformed Razor or CSS files directly from a generator would rely on unsupported side effects. Generator calls may be concurrent, repeated, cached, or cancelled in command-line and IDE compiler hosts. The compiler provides no output ownership, cleanup, atomic grouping, or dependency tracking for such files.

A write also does not alter the already-created `AdditionalFiles` collection, and generator ordering remains unspecified. The generated CSS would not become a `ScopedCssInput` for the same build.

### Analyzer-triggered restart or rebuild

Analyzers and generators expose no supported API to request project reevaluation, an MSBuild invocation, an application restart, or static asset recomputation. A diagnostic, exception, or touched project file is not a supported control channel.

`MetadataUpdateHandlerAttribute` is downstream of compilation. It runs after a metadata update has been produced and applied, so it cannot create a missing update or regenerate compiler inputs.

### Background watcher launched by MSBuild

An MSBuild task or node does not have the same lifetime as a `dotnet watch` or IDE session. Node reuse may be enabled or disabled, and graph, multi-targeted, normal, and design-time builds may load multiple task instances.

Relevant cache APIs do not provide watch-session ownership:

- [`IBuildEngine4.RegisterTaskObject`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.framework.ibuildengine4.registertaskobject)
- [`RegisteredTaskObjectLifetime`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.framework.registeredtaskobjectlifetime)

Likely failure modes include duplicate or orphaned watchers, package assembly locks, output races with real builds, recursive-build deadlocks, and filesystem event loops. Calling the build engine after a task returns is unsupported. A detached MSBuild watcher should not be shipped.

## Foreground Wrapper Fallback

A separate foreground development tool could potentially provide production-quality CLI-only support:

1. Evaluate the project and selected project-reference graph.
2. Generate a stable shadow Razor and CSS tree under `obj`.
3. Launch `dotnet watch` as a child process.
4. Watch authored files and atomically update their shadow files.
5. Let `dotnet watch` observe changes to the actual Razor and scoped CSS inputs.
6. Own cancellation, child termination, polling fallback, and cleanup.

This would not add an application runtime dependency, but it would have important limitations:

- Users would run a package-specific command instead of plain `dotnet watch`.
- It would not integrate with Visual Studio Hot Reload.
- Razor and CSS updates are not transactional from the child watcher's perspective.
- Adding or removing the first inline style changes project item topology and might require restart.
- Diagnostics and navigation could point into the shadow tree.
- Multi-targeting, project references, linked files, casing, network filesystems, and event coalescing add substantial complexity.

This option should be considered only after proving that changes to both existing shadow inputs are applied correctly without restarting the application.

## Recommended Upstream Capability

The smallest generic SDK capability that could preserve this architecture is watched-input metadata with semantics similar to:

```xml
<Watch Include="@(RazorScopedStyleElementsOriginalComponents)"
       ReevaluateOnChange="true" />
```

On a matching change, `dotnet watch` would need to:

1. Preserve the metadata in its in-process file model.
2. Rerun the affected project's design-time target set.
3. Refresh that project's `Compile` and `AdditionalFiles` documents.
4. Rediscover generated Razor and scoped CSS inputs.
5. Coordinate generated static web asset processing.
6. Compute Hot Reload deltas only after the refresh completes.

This is preferable to a general plugin callback. The package remains responsible for an incremental, design-time-safe MSBuild target, while the SDK owns project evaluation, workspace synchronization, and application lifecycle.

The closest existing request is:

- [dotnet/sdk#49934: add `ForceRebuild` metadata to `Watch`](https://github.com/dotnet/sdk/issues/49934)

That request also concerns an MSBuild generator. Its proposed full rebuild/restart behavior is useful as a fallback but is not state-preserving Hot Reload. RazorScopedStyleElements should add its use case there and explain the stronger reevaluation-and-refresh requirement. If maintainers consider that separate scope, a new `dotnet/sdk` issue should be filed under `Area-Watch`.

An MSBuild proposal for discovering files that affect evaluation is related but does not itself provide per-change target execution:

- [dotnet/msbuild#12001](https://github.com/dotnet/msbuild/issues/12001)

The longer-term Razor solution is first-class inline scoped styles:

- [dotnet/razor#10766](https://github.com/dotnet/razor/issues/10766)

A complete Razor feature should integrate parsing, source mapping, diagnostics, CSS extraction, scope assignment, bundling, static web assets, IDE snapshots, and Hot Reload invalidation rather than merely materializing a `.razor.css` file.

## Proof-of-Concept Plan

Run the smallest experiments in dependency order.

### 1. Existing shadow-input Hot Reload

Start with a component that has inline CSS during the initial build. Ensure the transformed Razor and generated CSS paths appear in `dotnet watch --list`, then run plain `dotnet watch`.

Edit each generated file directly and verify:

- Changed markup reaches the browser.
- Changed scoped CSS reaches the served bundle.
- The application process or startup identifier does not change.

This determines whether an external foreground bridge could work when item topology is stable.

### 2. One-shot source-to-shadow bridge

While `dotnet watch` is running, edit the authored component and invoke the existing transformation once from a separate short-lived process. Do not introduce a persistent watcher.

Verify that both generated events are consumed, then test rapid consecutive edits and Razor/CSS event ordering. This isolates transformation feasibility from process-lifecycle complexity.

### 3. Patched SDK reevaluation

In a private `dotnet/sdk` build, preserve reevaluation metadata on a watched item. When the authored component changes, rerun the affected design-time targets, refresh the workspace project, and only then calculate updates.

Verify transformed markup, `CssScope`, the CSS bundle, browser refresh, and unchanged application identity.

### 4. Topology and graph cases

Test:

- Markup-only and CSS-only edits.
- Adding the first inline style.
- Removing the last inline style.
- Component deletion and rename.
- Razor class libraries referenced by an application.
- Multi-targeted projects.
- Two projects with equal component-relative paths.
- Windows, Linux, and macOS path behavior.

## Automated Test Strategy

Most behavior should remain covered by deterministic build tests:

- Extraction and diagnostics.
- Logical path preservation.
- `RazorComponent` replacement and `ScopedCssInput` association.
- Incremental no-op writes.
- Stale CSS removal.
- Clean behavior.

A real process test is necessary for any claimed Hot Reload support. `dotnet watch --list` proves discovery only.

The integration harness should:

1. Create an isolated Blazor application.
2. Launch the watcher with redirected output and browser launch disabled.
3. Discover a dynamically assigned listening URL from process output.
4. Wait for readiness with a bounded cancellation token.
5. Record the application PID or a per-start identifier exposed by the test app.
6. Edit markup and styles.
7. Poll the rendered response and served stylesheet with explicit deadlines.
8. Assert that content changed without a new startup identifier.
9. Kill the entire process tree in `finally`.
10. Await termination with a second short timeout before deleting the temporary project.

Useful isolation settings include:

```text
DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1
DOTNET_CLI_USE_MSBUILD_SERVER=0
MSBUILDDISABLENODEREUSE=1
DOTNET_CLI_TELEMETRY_OPTOUT=1
```

No process read, HTTP poll, or exit wait should be unbounded. Early prototypes should run in an opt-in or nightly Windows, Linux, and macOS matrix. If testing a foreground wrapper, launch only the wrapper as the root test process and verify that disposing it terminates all descendants.

## Risk Summary

| Design | Security | Performance | Lifecycle | Maintenance |
| --- | --- | --- | --- | --- |
| Current MSBuild build integration | Low | Build-time scan | Build-owned | Moderate |
| Generator disk writes | Undeclared compiler-host writes | Repeated and unpredictable | Compiler-host-dependent | Very high |
| Background MSBuild watcher | Detached process execution | Duplicate watchers/builds | No reliable owner | Very high |
| Foreground wrapper | Explicit development tool | Continuous filesystem work | Explicit owner | High |
| Upstream SDK/Razor support | SDK-controlled | Coordinated by watcher | Watcher-owned | Lowest after adoption |

## Verified Facts and Open Assumptions

Verified from upstream source and the issue reproduction:

- An authored edit does not rerun this package's target during ordinary Hot Reload.
- The current watcher has no public per-file target, dependency mapping, reevaluation, or handler extension.
- A generator cannot replace Razor's `AdditionalText` or contribute `ScopedCssInput`.
- The Razor SDK source generator does not expose package registration for custom project-engine passes.
- .NET 11 Preview 7, `release/11.0.1xx`, and current `main` do not add the missing public extension.

Still requiring proof-of-concept validation:

- Whether coordinated updates to the two existing generated paths consistently produce state-preserving Razor and CSS Hot Reload.
- Whether an SDK project reevaluation can refresh generated Razor inputs and scoped static assets as one reliable update.
- Whether style addition and removal can avoid restart when generated item topology changes.
- Whether a CLI wrapper's value would justify its IDE and maintenance limitations.

Until those assumptions are validated or an upstream API lands, restart mode remains the only supported development workflow for edits to components transformed by RazorScopedStyleElements.
