# Local AI Widget Architecture

## Overview

This document describes the MVP architecture with special focus on runtime version logging, configurable polling, rolling 5-minute history, and per-runtime alert handling.[cite:5][cite:34][cite:40]

## Modules

### 1. Collector Core
- Schedules polling cycles
- Batches runtime requests in one cycle where possible
- Maintains bounded sample buffers
- Publishes normalized state

### 2. Runtime Adapters
- Ollama adapter
- llama.cpp adapter
- Each adapter emits runtime version metadata and unknown field discovery events

### 3. History Aggregator
- Converts raw bounded samples into a rolling 5-minute summary model
- Produces UI-friendly trend summaries

### 4. Alert Engine
- Evaluates runtime-specific conditions
- Applies severity levels
- Applies cooldowns per alert type
- Keeps Ollama and llama.cpp alert state separate

### 5. Diagnostics Exporter
- Serializes current state and recent history into a snapshot
- Includes runtime versions, polling configuration, and unknown fields

## Data Flow

1. Collector polls system metrics and active runtime adapters.[cite:5][cite:34][cite:40]
2. Adapters normalize runtime state and emit version metadata.
3. Unknown fields are sent to diagnostics buffers.
4. History aggregator computes last-5-minutes summaries.
5. Alert engine evaluates per-runtime rule sets.
6. UI renders compact or expanded state.
7. Diagnostics exporter writes a snapshot on demand.
