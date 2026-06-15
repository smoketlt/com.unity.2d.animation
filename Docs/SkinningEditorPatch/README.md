# Unity 2D Animation Skinning Editor Patch Knowledge Base

This is the canonical knowledge base for the forked `com.unity.2d.animation` package used by the Skinning Editor patch project.

## How To Read

1. Open [Index](Index.md).
2. Route to the relevant subsystem page.
3. Read the entity page for the class or workflow being changed.
4. Open source files only after the documentation has given the expected architecture and invariants.

## What Lives Here

- package and fork workflow notes;
- Skinning Editor architecture maps;
- subsystem pages for geometry, toolbar, input, copy/paste, and mesh data;
- entity pages for the main classes touched by the fork;
- change-risk notes and verification checklists;
- documentation update rules for future tasks.

## Update Rules

- Every non-trivial behavior change must update the relevant docs in the same task.
- If a new workflow, shortcut, toolbar command, or modal dialog is added, update `Index.md` and the relevant subsystem page.
- If behavior crosses several classes, update every related entity page.
- If source inspection reveals that a doc page is stale, fix the doc before relying on it for implementation decisions.
