# ModelPulse UI Spec

## Overview

This document describes the MVP interface behavior for ModelPulse. It includes runtime version visibility, advanced polling settings, a 5-minute history view, and runtime-aware alert handling.[cite:5][cite:34][cite:40]

## Compact Overlay

The compact overlay is the always-on-top view used during active work.

### Required elements
- CPU summary
- RAM summary
- GPU summary
- Active runtime badge
- Last t/s value
- Busy or idle state

### Behavior
- Runtime-specific detail rows appear only when their runtime is detected.[cite:5][cite:40]
- If more than one runtime is active, the compact overlay shows a short runtime summary rather than full details.
- Clicking expands into the expanded overlay.

## Expanded Overlay

The expanded overlay is the primary monitoring surface.

### Default runtime behavior

If one runtime is active, show that runtime directly.

If more than one runtime is active, default to a **runtime toggle** at the top of the runtime section so the user views one runtime at a time. Collapsible sections may be available as an optional alternative, but they should not be the default multi-runtime layout.

### Runtime panel contents

Each runtime panel should show:
- Runtime name
- Runtime version
- Availability state
- Active model details
- Context-related fields when available
- Throughput metrics
- Runtime-specific warnings

## Polling Settings UI

The settings panel must expose polling controls beyond low-power mode.

### Modes
- Default
- Low-power
- Custom

### Custom mode fields
- Idle refresh interval
- Active refresh interval
- Warning text explaining higher resource cost
- Reset to recommended defaults

## History View

The expanded overlay must include a **Last 5 Minutes** section instead of showing raw sample arrays.

The section should use lightweight visuals or summaries for:
- CPU trend
- RAM trend
- GPU trend when available
- Runtime throughput trend
- Recent pressure events

## Alerts UI

Alerts must be runtime-aware.

### Rules
- Ollama alerts and llama.cpp alerts must remain distinct.
- Repeated alerts of the same type must respect cooldowns.
- Critical alerts may surface prominently.
- Info-level alerts should remain quiet or live inside the expanded view.

## Diagnostics Export UI

The tray menu or settings panel must offer a diagnostics export action.

The export should include visible confirmation that the snapshot contains:
- Runtime versions
- Polling configuration
- Unknown field log entries
- Current alert summary
