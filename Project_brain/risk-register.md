# ModelPulse Risk Register

## Overview

This register tracks key implementation and operational risks for the ModelPulse MVP.[cite:5][cite:34][cite:40]

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R-01 | Runtime field drift breaks parsing | High | High | Use schema validation, null-safe parsing, and unknown field logging |
| R-02 | Runtime version mismatches are hard to debug | Medium | High | Store runtime version data in every diagnostics snapshot |
| R-03 | GPU telemetry differs by vendor | High | High | Use compatibility matrix and unavailable states instead of fake zeroes |
| R-04 | High custom polling increases overhead | Medium | High | Make high refresh opt-in and show warnings in settings |
| R-05 | Raw sample data is hard for users to interpret | Medium | Medium | Provide a last-5-minutes summary view |
| R-06 | Multiple runtimes create overlapping alerts | Medium | Medium | Add per-runtime alert rules and per-alert cooldowns |
| R-07 | Diagnostics miss useful new fields | Medium | Medium | Log unknown fields instead of discarding them |
