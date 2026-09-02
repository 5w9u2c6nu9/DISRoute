# DISRoute handoff — 2026-09-02

This directory records the ChatGPT handoff/upload preservation state from the 2026-09-02 DISRoute session.

## Branch

`handoff-upload-bundle-2026-09-02`

This branch is intentionally separate from `main` so the authoritative DISRoute 1.0.0-rc.1 / Pass 30 baseline is not altered.

## Authoritative baseline

- Release: DISRoute 1.0.0-rc.1 / Pass 30
- Commit: `4323a320d10662d5ba96ae84ab88b6fa6cad6d5a`
- Tree: `6e7530befb9b514daf4fe64ec0fdbba5705cc4f8`
- Source ZIP SHA-256: `029865783d276c0da28c2b066bd218462802b2f408dfe9eb5e60f77e865a9898`
- Do not overwrite or rewrite the Pass 30 baseline from this handoff branch.

## Uploaded build-file set

The conversation contained 52 original build/source dependency files. `UPLOAD-INVENTORY-SHA256.txt` records the exact filename and SHA-256 of every file.

The connected GitHub API available in this chat can create UTF-8 repository files but does not expose binary-file, Git LFS, or GitHub Release asset uploads. Therefore the NuGet packages, SDK/runtime archives, PostgreSQL bundle, and source ZIP are **not falsely represented as committed here**.

Two files also exceed GitHub's normal 100 MB per-file repository limit:

- `dotnet-sdk-10.0.400-linux-x64.tar.gz` — 240,133,692 bytes
- `percona-postgresql-18.6-ssl3.5-linux-x86_64.tar.gz` — 321,299,197 bytes

The inventory/checksums in this directory are intended to make the original offline dependency set auditable and reproducible.

## Screenshot provenance rule

A DISRoute image may only be called a real screenshot when the exact build was compiled, launched, and the pixels captured from that running process. Generated images must be labeled concept/not implemented.

## Latest verified post-RC Classic CAD revision

The latest verified Classic CAD experiment enlarged Call Entry from 68 px to 154 px, enlarged Nature/Address/Priority fields, moved Caller/Callback/Station into a second row, added prominent Check Duplicates/Create Incident actions, and built with 0 warnings / 0 errors before being launched for capture. This remains post-RC work until folded into a fully qualified future baseline.
