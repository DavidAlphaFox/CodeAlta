# CodeAlta User Guide

An agentic AI coding CLI assistant developed in .NET.

## Infrastructure Status

Current infrastructure-first progress includes workspace bootstrapping primitives:

- `CodeAlta.Workspaces`: workspace/project descriptors, machine override profiles, catalog loading.
- Scope resolution (`global`, `workspace`, `project`) into concrete checkout and `.codealta` roots.
- Checkout planning (`clone` vs `update`) without network side effects.

## Workspace Descriptor Layout

Global repository layout (implemented reader support):

- `workspaces/<workspaceKey>/workspace.yaml`
- `workspaces/<workspaceKey>/projects/*.yaml`
- `machines/<machineId>.yaml`

The YAML model uses UUID v7 strings for workspace/project `id` values and validates
workspace/project keys using `^[a-z0-9][a-z0-9\\-_.]{1,63}$`.
