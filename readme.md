# CodeAlta [![ci](https://github.com/xoofx/CodeAlta/actions/workflows/ci.yml/badge.svg)](https://github.com/xoofx/CodeAlta/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/CodeAlta.svg)](https://www.nuget.org/packages/CodeAlta/)

<img align="right" width="160px" height="160px" src="https://raw.githubusercontent.com/xoofx/CodeAlta/main/img/CodeAlta.png">

An agentic AI coding CLI assistant developed in .NET.

## ✨ Features

- Workspace descriptors and machine profile overrides (`CodeAlta.Workspaces`)
- Scope resolution and checkout planning APIs for multi-repo workspaces
- SQLite-backed durable state + migrations + repositories (`CodeAlta.Persistence`)
- Markdown artifact store with YAML frontmatter parsing and plain-text extraction
- FTS5 + embedding-backed hybrid search pipeline (`CodeAlta.Search`)
- In-process MCP server surface with tools for tasks, artifacts, search, workspaces, and agents (`CodeAlta.Mcp`)
- Agent orchestration services for role profiles, context packs, and planner/builder durable workflows (`CodeAlta.Orchestration`)
- In-memory MCP transport tests for tool discovery and roundtrip tool calls

## 📖 User Guide

For more details on how to use CodeAlta, please visit the [user guide](https://github.com/xoofx/CodeAlta/blob/main/doc/readme.md).

## 🪪 License

This software is released under the [BSD-2-Clause license](https://opensource.org/licenses/BSD-2-Clause). 

## 🤗 Author

Alexandre Mutel aka [xoofx](https://xoofx.github.io).
