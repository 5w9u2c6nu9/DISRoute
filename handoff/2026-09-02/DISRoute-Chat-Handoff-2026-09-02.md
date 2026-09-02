# DISRoute — Chat Handoff
**Date:** 2026-09-02

## Critical instruction to the next chat

Continue DISRoute from the state described below. **Do not restart, redesign, or re-plan the project from scratch.**

Keep the following three states distinct:

1. **Authoritative release candidate:** DISRoute **1.0.0-rc.1 / Pass 30**
2. **Verified post-RC Classic CAD UI experiments/revisions**
3. **Pass 31 conformance/feature-expansion work-in-progress**, which is NOT yet an authoritative checkpoint

Do not merge claims from #2 or #3 into Pass 30 unless they have actually been rebuilt, tested, packaged, and promoted.

---

# 1. Authoritative DISRoute baseline — Pass 30

Current authoritative release:
**DISRoute 1.0.0-rc.1**

Final Git state:
- Commit: `4323a320d10662d5ba96ae84ab88b6fa6cad6d5a`
- Tree: `6e7530befb9b514daf4fe64ec0fdbba5705cc4f8`
- Direct parent Pass29: `8ae9c48a8feb7aa465e22bfc2e818b5a56d7dfd3`
- Commit message: `Establish authoritative DISRoute 1.0.0-rc.1 baseline`
- `main` and `pass-30` point to the same commit.

Final frozen source shape:
- 330 source files
- 124 C# files
- 18 projects
- 18 `packages.lock.json`
- 313 API routes
- 20 PostgreSQL migrations

Authoritative source ZIP:
`DISRoute-Implementation-Pass-30-2026-08-22.zip`

Source ZIP SHA-256:
`029865783d276c0da28c2b066bd218462802b2f408dfe9eb5e60f77e865a9898`

Deployment ZIP:
`DISRoute-1.0.0-rc.1-deployment.zip`

Deployment ZIP SHA-256:
`471d1a517fbe57034cc48078e577e8151fc78cc905c8b21a0e27d566f98c36a0`

Other final hashes:
- deployment manifest: `5644ed3832b0793a0595852d676b8b0e67b1e7f5e23d130f5915eab176f2f93c`
- SBOM: `37b96927fdce70f2615dcc537f4eaad023f72bf42f6f8e86ee38c664f9b9b569`
- RC release manifest: `cf14595db5651c3e0308f855798b207e6567b7855d549a07e4e739c4be724c1b`
- Pass30 cumulative manifest: `7bfc0535872e163803d251c65fe1cffb26f1146b3e3838ce7a17fab5d521d570`

Persistent Library source:
`/DISRoute Checkpoints/DISRoute-Implementation-Pass-30-2026-08-22.zip`

Do not modify or overwrite the authoritative Pass 30 artifact.

---

# 2. Pass 30 verified qualification

Actually run and passed:
- locked restore 18/18 projects
- Release build: 0 warnings / 0 errors
- SelfTest: `incidents=7, units=6, audit=145, alerts=3`
- source verifier: `124 C# / 18 projects / 313 API routes / 20 migrations`
- FileCheckpoint restart/backup/staged restore
- Pass28 operational E2E: 6/6
- Pass29 stress/hostile-input
- five Avalonia startup smokes
- PostgreSQL 18.6-compatible qualification
- application HA and active-incident authority transfer
- active-load crash failover
- production security/TLS qualification
- physical PostgreSQL HA/PITR qualification
- 8-target publish matrix
- dependency integrity
- publish reproducibility
- deterministic deployment packaging
- SBOM/package integrity
- source ZIP reproducibility

Do not overclaim:
- no certified production STONITH/quorum implementation
- no production geographic SLA/RTO/RPO certification
- no live conformance for every vendor/provider
- no signed installer certification
- no Pi hardware acceptance unless actually tested
- no independent penetration/accessibility certification
- no agency operational/go-live acceptance

---

# 3. Product architecture that must remain unchanged

- Product name: **DISRoute**
- Never reintroduce legacy product/vendor branding into production code/UI.
- Windows-first interactive operational clients.
- Linux ARM64 Raspberry Pi only for lightweight Station Display/wallboards.
- Server supports Windows Server or Linux.
- API-authoritative architecture; clients do not directly connect to PostgreSQL.
- PostgreSQL production persistence.
- SignalR/WebSockets realtime.
- HA/DR authority: **Dispatch Primary → Dispatch Local Standby → HQ DR**
- Exactly one authoritative writer.
- Fail closed on authority ambiguity.
- Dispatch-only integrations: 911, radio, Eventide/recording.
- Automatic geographic promotion disabled by default.
- Mobile radio is listen-only; no PTT.
- Themes required: Classic / Light / Dark.
- Densities required: Compact / Standard / Comfortable.
- Status must never be conveyed by color alone.

Major scope includes: CAD, Station, Operations Center, Station Display, Mobile/offline, RMS/NERIS, Workforce, DMS/forms, chat, GIS/routing, preplans/CRG, ONVIF, AIS, 911/radio/Eventide, HA/DR, integrations, deployment/support tooling.

---

# 4. Screenshot provenance rule — IMPORTANT

The user specifically raised concerns about whether screenshots were real or generated.

From now on:

## REAL APP SCREENSHOT
Only call an image a DISRoute screenshot if:
- the exact build was actually compiled,
- the process was actually launched,
- the screenshot was captured from that running app.

Prefer preserving:
- source/checkpoint identity
- executable SHA-256
- build result
- launch command/process evidence
- server endpoint
- screenshot SHA-256

## GENERATED CONCEPT
Must be labeled: **AI-GENERATED CONCEPT / NOT IMPLEMENTED**

## PARTIALLY IMPLEMENTED UI
Must clearly state which elements are real and which are placeholders.

Do not call generated concept images screenshots.

Earlier generated Station Board images with camera/map/crew roster were concepts, not running-app screenshots.

---

# 5. Real Pass 30 application screenshots

Fresh real captures were made from actual launched Pass 30 Avalonia clients:
- CAD
- Station
- Operations Center
- Station Display
- Mobile

The captures deliberately retained real degraded/error states rather than polishing them out.

Screenshot provenance record: `/mnt/data/pass30-capture/PROVENANCE.txt`

The real Pass 30 Station Display is much simpler than the generated Station Board concepts.

The actual Station Display at Pass 30 does NOT yet contain the full camera/map/crew-roster/physical-control board shown in concept art.

---

# 6. Classic CAD post-RC work

Classic CAD is a **post-RC UI experiment/revision**, not part of frozen Pass 30.

Persistent Library folder: `/DISRoute Checkpoints/Installers/1.0.0-rc.1/Classic UI Preview/`

Preserved artifacts include:
- `DISRoute-CAD-1.0.0-rc.1-classic-linux-x64.run`
- `DISRoute-CAD-Classic-UI-Preview.zip`
- `DISRoute-CAD-Classic-Linux.png`

Classic direction:
- modernized old-school dispatch/CAD visual language
- dense information
- hard/square panels
- reduced whitespace
- persistent call queue
- persistent unit board
- selected incident and narrative visible together
- keyboard/function-key orientation
- strong operational status colors
- themes must not invent functionality

Classic is a visual/density mode, not a separate feature set.

The user wants Classic mode to resemble the old operational CAD lineage more clearly while remaining modern and usable.

---

# 7. Latest verified Classic CAD revision — larger Call Entry

The user asked for Call Entry to be bigger and more front-and-center.

A real source revision was created from the preserved Classic CAD source and rebuilt using the exact pinned RC toolchain.

Changes:
- Call Entry height increased from **68 px to 154 px**
- larger Nature and Address controls
- much wider Address field
- larger Priority selector
- Caller / Callback / Station moved into a proper second row
- large `CHECK DUPLICATES` control
- dominant green `CREATE INCIDENT` action
- Active Incidents, Incident Workspace and Unit Board remain visible underneath
- no unsupported call fields were invented

Build result: **0 warnings / 0 errors**

The exact app was launched against a real local DISRoute server before capture.

Latest real screenshot: `/mnt/data/classic-call-entry-work/release/DISRoute-CAD-Classic-Large-Call-Entry.png`

Revision bundle: `/mnt/data/DISRoute-CAD-Classic-Large-Call-Entry-Revision.zip`

The revision bundle contains the source diff/provenance.

This is currently the best verified Classic CAD visual baseline.

---

# 8. Known CAD visual issue

The user noticed incorrect text/foreground colors in an earlier real CAD screenshot.

This is a real theme/foreground-resource problem, not an image-generation issue.

The fix should:
- use consistent explicit CAD foreground brushes
- correct incident list text
- unit board text
- narrative/activity text
- form labels
- tab headers
- selected/unselected row contrast
- degraded/error/disabled text contrast

Do not merely retouch the screenshot.

---

# 9. Audit findings that must not be forgotten

Important findings included:
- empty production PostgreSQL must NOT silently persist development/demo seed data
- several writes needed stronger explicit permission coverage
- some client-supplied Actor/RequestedBy fields should not be trusted for authoritative audit identity
- `/api/me` had demo/default-agency assumptions
- clients contained hidden DEMO/default agency/station fallbacks
- some integration simulator controls could look like production features
- multi-agency support was incomplete in deeper operational paths
- themes/densities existed in data but were not fully implemented at runtime
- richer Station Board and GPIO features were concept-only at the time of audit

Do not call conceptual functionality implemented until there is actual code and runtime verification.

---

# 10. Pass 31 WIP — NOT authoritative

A large Pass 31 conformance/feature-expansion effort was started.

Important: **Pass 31 is WIP and was not promoted to an authoritative checkpoint.**

Work discussed/implemented in WIP included:
- production-safe first-run setup
- stronger multi-agency foundation
- per-agency module state
- training/simulation subsystem
- health/degraded operations
- capability matrix
- Station Display hardware monitoring
- kiosk/watchdog policy
- physical-control diagnostics
- keyboard command profiles
- operator/shift handoff
- maintenance/OOS tracking
- configuration validation/snapshots/rollback
- update management
- support bundle
- emergency sheets
- retention preview
- global search
- human-readable audit
- Operations Center Platform workspace
- real appearance framework
- richer Station Board projection

However: **do not treat all of those as final/qualified production features yet.**

The WIP had source-structure verification at one point around:
- 130 C# files
- 18 projects
- ~361 API routes
- 20 migrations

But a final complete Pass 31 qualification was NOT completed.

Required before promoting Pass 31:
- exact SDK 10.0.400 locked restore
- all 18 Release projects
- 0 warnings / 0 errors
- SelfTest
- source verifier
- restart/backup/restore
- five Avalonia startup smokes
- true two-agency isolation tests
- Linux/Pi publishes
- Station Display ARM64 test
- final package/manifests/hashes

Do not overwrite Pass 30 until those gates are green.

---

# 11. Station Display / GPIO truth

Desired direction:
- actual station board
- current assignments
- apparatus
- crew
- alerts/bulletins
- camera status
- listen-only radio status
- preplan/CRG indicators
- physical ACK/SILENCE/REPEAT buttons

GPIO concept:
- ACK → BCM 17
- SILENCE → BCM 27
- REPEAT → BCM 22
- active-low dry-contact to GND
- Pi GPIO is 3.3 V only
- industrial/12/24 V controls require isolation/interface hardware

Semantics:
- ACK must be server-authoritative and auditable
- SILENCE is local only
- REPEAT is local only
- physical inputs should invoke the same DISRoute commands as UI controls, not emulate mouse clicks

But only call GPIO hardware qualified after real Pi hardware testing.

---

# 12. Installer state

DISRoute installers exist outside the frozen source tree.

Windows Workstation Suite installs:
- CAD
- Station
- Operations Center

Windows Server installer has:
- Server Only / External PostgreSQL mode
- All-in-One Local Test mode
- private .NET runtime
- optional PostgreSQL 18.6-1 setup
- dedicated PostgreSQL service/port
- local app binding and readiness check

Linux:
- server `.run`
- Station Display ARM64 `.run`
- Linux CAD preview `.run`

Windows installer click-through was not certified from the Linux environment.

---

# 13. Current design priority

Immediate visual priority: **Classic CAD**

Current user preference:
- keep Call Entry large and prominent
- preserve call queue and unit board
- dense operational layout
- less modern-card styling
- closer to classic dispatch/CAD ergonomics
- fix text/foreground contrast properly in source
- only show new screenshots after actual build/launch

The latest enlarged Call Entry screenshot should be the visual starting point, not the older Classic screenshots.

---

# 14. Recommended next action in a new chat

Start by saying that you are continuing from this handoff.

Then:
1. Locate/use the authoritative Pass 30 source.
2. Locate the latest Classic CAD large-Call-Entry revision bundle.
3. Apply the text/foreground contrast fix to the actual Classic CAD source.
4. Rebuild with exact .NET SDK 10.0.400 and locked dependencies.
5. Launch against a real local DISRoute server.
6. Capture a fresh real screenshot.
7. Preserve provenance.
8. Only after the Classic CAD UI is approved, continue folding the verified changes into a properly qualified Pass 31 baseline.

Do not substitute concept art when the user asks for a screenshot.

---

# User preference for this project

The user wants:
- actual implementation, not planning-only responses
- real installers and real screenshots
- exact verification
- no invented features
- no hallucinated implementation claims
- no generated image presented as a real screenshot
- authoritative checkpoints preserved
- concise progress updates during long work
- minimal unnecessary clarification
