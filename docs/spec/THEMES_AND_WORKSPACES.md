# Themes, Skins, Docking, Multi-Monitor, and Roaming Workspaces

## Theme framework

Classic, Light, and Dark are built-in defaults, not a closed set. Themes target a stable versioned contract with semantic resources for:

- backgrounds/surfaces;
- text/secondary text;
- borders/dividers;
- typography;
- spacing/density;
- panel/header styles;
- table/grid styles;
- focus/selection;
- semantic operational statuses;
- alert severity;
- icons/assets where permitted;
- agency branding/logo slots.

External theme packages can be installed/imported, validated, previewed, enabled/disabled, and upgraded independently. Safety-critical contrast/status meaning and training-mode identification remain platform enforced.

## Density

Density is separable from theme: Compact, Standard, Comfortable. Dispatch defaults should favor Compact/Standard without forcing all users into the same choice.

## Multi-monitor requirement

Design for 2–4+ displays from day one. Major panels can float as fully interactive windows on any monitor. Mixed DPI, different resolutions, portrait/landscape, display reordering, dock/undock and monitor removal must be handled safely.

Examples of logical monitor roles:

- Primary CAD
- GIS/Map
- Resources
- Auxiliary/Messaging/Replay

## Saved layouts

Support named layouts such as Single Monitor, Dual Monitor, Supervisor, Map Heavy, Call Taker, and user-created presets.

Persist logical arrangement rather than raw absolute coordinates:

- panel groups/tabs;
- sizes/proportions;
- floating/docked state;
- monitor role preference;
- theme/density;
- grid/column preferences;
- map/layer preferences where appropriate.

## Roaming profiles

Layouts are centrally stored and follow the user across workstations. Restore precedence:

1. user + workstation override;
2. user roaming layout;
3. agency/role layout;
4. factory default.

Adapt to monitor topology. If fewer monitors exist, collapse/re-dock safely; when richer topology returns, restore the preferred arrangement.

## Recovery controls

Always provide:

- Bring All Windows Here
- Reset Workspace
- Restore Agency Default
- Save Workspace As

Never leave windows stranded off-screen.

## Global alert visibility

Critical operational alerts/core connectivity status remain visible/mirrored across active DISRoute desktop windows so a detached panel cannot hide a Mayday/core failure.
