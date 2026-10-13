# Unity Agent Tooling

## Scope and installation

This repository is the `com.unity.2d.animation` fork. Unity CLI is user tooling; `com.unity.pipeline` belongs to the consuming Unity project, not this package's dependencies. Keep the fork identity, documentation-first workflow and Unity 6000.0/6000.5 compatibility rules intact.

Setup performed on 2026-10-13:

- Existing CLI verified: `1.0.0-beta.11`, at `C:/Users/Mike/AppData/Local/Unity/bin/unity.exe`.
- Pipeline installed through `unity pipeline install`: `com.unity.pipeline` `0.8.0-exp.1`.
- Target project: `G:/Users/Mike/Desktop/===DELETE ME====/Cube lines/2D Animation Skinning Edoitor Patch`, running Unity `6000.0.81f1`. This is the observed local verification project; the repository's historical target remains `6000.0.7`.
- The target manifest consumes this checkout through `file:C:/Users/Mike/Documents/Unity Skinning Editor`; edits should recompile directly from this checkout.
- All 34 skills from [Unity-Technologies/skills](https://github.com/Unity-Technologies/skills) installed to `C:/Users/Mike/.codex/skills`, source revision `9d282100540601cfddc4bbd69f36a239d7a6f55c`.
- All three skills from [ArkTarusov/unity-cli-plugin](https://github.com/ArkTarusov/unity-cli-plugin) installed to the same directory, source revision `1b8611dbd61eef663d4aa65d1b289cf5c033b5ad`.
- ArkTarusov's `unity-pipeline` directory and frontmatter name were changed to `unity-pipeline-operations` to coexist with the official skill. Its other skills retain `unity-cli-core` and `unity-editors`. This is the repository's documented Codex skills installation method; no separate marketplace plugin registration was performed.

New skills become available on the next Codex turn. Unity's bundled plugin was already present; explicitly installed upstream copies are the refreshed source for this setup. Prefer the upstream copy when both are visible, and do not execute both copies of a workflow.

## Routing and command conventions

Read the project index first, then the relevant subsystem page. Read the matching skill before automation:

- Official `unity-cli`: current CLI command reference.
- Official `unity-pipeline`: live Editor compile/test/control loop.
- Official `unity-package-management`: UPM package selection and management.
- ArkTarusov `unity-cli-core`: binary discovery, structured output and diagnostics.
- ArkTarusov `unity-editors`: Editor/modules/licenses/projects.
- ArkTarusov `unity-pipeline-operations`: command arguments, asynchronous operations and recovery.

The live command schema and installed CLI help take precedence over examples from another beta. Every `unity command` call must carry `--caller plugin --skill <active-skill>`. Use `--format json --no-banner --non-interactive`, and explicitly set `--project-path` for the intended Editor. Editor parameters generally use `--snake_case_name value`; inspect the command schema and verify echoed parameters rather than assuming undocumented syntax.

```powershell
unity --version
unity pipeline list --format json --no-banner --non-interactive
unity status --format json --no-banner --non-interactive
# Substitute the actual consuming Unity project's absolute path.
unity command --caller plugin --skill unity-cli --project-path '<project>' --format json --no-banner --non-interactive
unity command editor_status --caller plugin --skill unity-pipeline --project-path '<project>' --format json --no-banner --non-interactive
```

Discover available commands first. Prefer live Editor commands for scenes, GameObjects and assets. After editing C# on disk, request refresh/recompile and poll its status until complete before attaching types or running relevant tests. Poll asynchronous test/package/build operations through their corresponding status commands. Do not treat an accepted asynchronous request as completed verification.

## Recovery and verification limits

The install command succeeded and the manifest contains `com.unity.pipeline: 0.8.0-exp.1`. The final `unity pipeline list` confirmed the installed version and reported no Safe Mode, but the Pipeline server remained unreachable. Live command execution and package compilation are therefore not yet verified. Switch focus to Unity so it refreshes the changed manifest, then repeat the status and command-discovery checks above. If resolution fails, inspect Package Manager and project-scoped logs before changing versions.

A restrictive sandbox can report no Editors while one is running. In this setup an unsandboxed `unity pipeline list` identified the correct Editor after the sandboxed call could not inspect processes. Use an appropriately authorized local check before interpreting an empty result as a closed Editor.

If a connection remains unavailable, inspect `unity pipeline list` for Safe Mode and pending loading, and filter compile errors from the project's Editor log. Preserve user work: do not switch dirty scenes without saving or explicit discard authorization, terminate all Editors, or delete locks blindly. A restart must account for unsaved work.

CLI and package upgrades are separate changes. Verify available versions and compatibility before an authorized upgrade. Preserve the renamed ArkTarusov skill on refresh; do not overwrite the official `unity-pipeline`. Do not mirror a package-provided skill onto this name without reviewing the collision. Run `git diff --check` for project documentation changes.
