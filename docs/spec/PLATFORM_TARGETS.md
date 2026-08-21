# DISRoute Platform Targets and Deployment Profiles

## Baseline platform decision

DISRoute is **Windows-first for full interactive operational workstations**. Linux compatibility is not a requirement for every desktop client and must not dilute the Windows dispatcher/station workstation experience.

Raspberry Pi/Linux ARM64 is a **dedicated wall-display target only**. A Raspberry Pi must not be treated as the default or supported substitute for a full dispatcher, supervisor, station workstation, Admin, Records, Workforce, or other interactive desktop role.

The authoritative DISRoute server/backend is supported on **either Windows Server or Linux**. Core server behavior, APIs, authorization, realtime semantics, data integrity, provider contracts, audit, and HA/DR behavior must remain equivalent across supported server operating systems. Individual vendor integration adapters may impose an OS requirement and must be isolated behind provider/gateway boundaries.

## Supported role matrix

| Role / Component | Windows | Linux x64 | Raspberry Pi / Linux ARM64 | Direction |
|---|---:|---:|---:|---|
| DISRoute CAD dispatcher/call-taker | **Primary / required** | Not a primary target | No | Optimize and certify on Windows. |
| DISRoute Supervisor | **Primary / required** | Not a primary target | No | Windows operational console. |
| Full DISRoute Station workstation | **Primary / required** | Not a primary target | No | Interactive firehouse workstation is Windows. |
| DISRoute Admin / Operations Center | **Primary / required** | Not a primary target | No | Full administrative desktop is Windows. |
| RMS / Workforce / Documents desktop workflows | **Primary / required** | Not a primary target | No | Full interactive desktop workflows are Windows-first. |
| DISRoute Station Display / wallboard | Supported where useful | Optional secondary | **Primary embedded target** | Read-mostly wall display only. |
| DISRoute Server / API / workers | **Supported** | **Supported** | No production-core target | Agency may choose Windows Server or Linux. |
| PostgreSQL/database tier | Supported deployment stack | Supported deployment stack | No | Follow the validated production database/HA topology. |
| Vendor integration gateway/adapter | As provider requires | As provider allows | Only for an explicitly supported edge adapter | Never force the entire platform onto one OS because of one SDK. |
| Development / CI | Required validation | Required validation | Required Station Display publish validation | Use a multi-target build/test matrix. |

`Not a primary target` means the architecture should avoid gratuitous OS coupling, but DISRoute does not promise or prioritize a supported Linux desktop build for those roles unless a later decision explicitly adds one.

## Windows workstation requirements

Windows is the reference environment for dispatcher consoles, supervisor consoles, station workstations, and administrative/records/workforce desktops. These clients may deliberately use Windows capabilities when they improve operations, including:

- Windows authentication/Kerberos/Negotiate where configured;
- dense keyboard-first workflows and workstation shortcuts;
- high-DPI multi-monitor docking/floating behavior;
- workstation session lock/unlock handling;
- local printing/peripheral integration where appropriate;
- signed Windows packaging/installation/update paths;
- vendor desktop SDKs only when isolated from shared/domain code.

Shared domain, application, contracts, provider interfaces, and server logic must not acquire Windows-only dependencies merely because the primary desktop client is Windows.

## Raspberry Pi wall-display profile

Raspberry Pi support is specifically for **read-mostly station wall displays / information screens**. The preferred target is Raspberry Pi OS 64-bit / Linux ARM64. Raspberry Pi 4 and Pi 5 are candidate hardware classes; final minimum hardware is established by performance testing rather than assumption.

The Station Display profile must support:

- full-screen/kiosk presentation with no normal desktop interaction required;
- automatic startup after boot;
- watchdog/restart behavior;
- automatic realtime reconnect and state reconciliation;
- station/device identity and centrally scoped configuration;
- cached current incident and relevant station operational state so a brief network outage does not blank the screen;
- cached preplan/CRG/map derivatives where policy/storage permits;
- display resolution/orientation detection and safe layout scaling;
- remote health/version/clock/connectivity reporting to Operations Center;
- centrally managed, signed update packages with staged rollout/rollback capability;
- no locally stored agency-wide provider credentials;
- clear offline/degraded/stale indicators;
- read-mostly permissions by default and no assumption that a wall display can perform full station-workstation actions.

The wall-display role may show current calls, apparatus/status, map, preplan/CRG indicators, weather/warnings, approved camera/radio-feed status, agency announcements, and other configured station information. Features are capability/module aware and disappear cleanly when disabled or not configured.

## Station workstation vs Station Display

These are separate deployment profiles even if they reuse shared Avalonia components:

**Full Station Workstation**
- Windows;
- interactive;
- authorized messaging, preplan/CRG navigation, station configuration permitted by role, acknowledgements, printing controls, and other station workflows.

**Station Display / Wallboard**
- Raspberry Pi/Linux ARM64 primary embedded target;
- read-mostly;
- automatic incident focus/takeover display;
- minimal local interaction;
- centrally managed and monitored.

Do not silently expand Raspberry Pi scope into a full Station workstation. That requires a new explicit product decision.

## Server operating systems

DISRoute Server must be deployable on either:

- supported Windows Server releases; or
- a supported Linux server distribution.

The server should use portable ASP.NET Core/.NET services and provider boundaries. OS-specific hosting details belong in deployment adapters/packages, for example Windows Service hosting versus `systemd` service units.

A production HA cluster may standardize on one validated OS profile for operational simplicity. Supporting both Windows Server and Linux does **not** require every individual HA deployment to mix operating systems in the same cluster.

## Required build/test matrix

CI and release engineering must prevent an accidental platform regression. At minimum, once the applicable projects exist:

1. **Windows x64** — build/test all primary desktop clients and server; run Windows-specific UI/integration tests.
2. **Linux x64** — build/test/publish the server/backend and portable shared libraries.
3. **Linux ARM64** — build/publish/test the Station Display profile and its shared dependencies.

Tests must fail when a shared/server dependency unexpectedly becomes Windows-only or when a Windows desktop change breaks the Station Display ARM64 build.

Platform-specific code must be explicit, isolated, and documented. Do not use `OperatingSystem.IsWindows()` branches throughout domain/application logic as a substitute for proper interfaces.

## Packaging direction

Release artifacts are role/OS specific:

- signed Windows desktop installers/packages for full operational clients;
- Windows Server and Linux server deployment packages/images as supported by the release process;
- signed Linux ARM64 Station Display packages/images for managed Raspberry Pi deployment.

Server configuration, agency module state, themes, permissions, and station display layouts remain centrally managed and are not hard-coded into platform-specific installers.
