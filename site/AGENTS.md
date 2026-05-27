---
discard: true # Disable this page for website generation
---

# Website (lunet) Contribution Instructions

This folder contains the static end-user website for CodeAlta, built with **lunet**.

Single source of truth for the overall project: read and follow `../AGENTS.md`.

## Structure

- `site/readme.md` -> home page (`/`)
- `site/docs/**` -> end-user documentation (`/docs/**`)
- `site/docs/menu.yml` -> documentation sidebar
- `site/menu.yml` -> top navigation
- `site/img/` -> site images and future screenshots/demo videos
- `site/.lunet/build/**` -> generated output; do not edit by hand

## Build & Serve

Install lunet once if it is not already available:

```sh
dotnet tool install -g lunet
```

Run from this folder after changing `site/**`:

```sh
lunet build
lunet serve
```

## Content Conventions

- Keep this site end-user focused: installation, configuration, workspace usage, providers, sessions, plugins, and troubleshooting.
- Keep internal specifications and implementation notes in `../doc/**`, not on the public site.
- Pages use Markdown with YAML front matter (`title` required).
- Navigation is defined in `menu.yml` files; update the relevant menu when adding or moving pages.
- Keep examples short, correct, and copy-pasteable. Avoid undocumented future behavior unless it is clearly presented as a planned screenshot/media slot.
