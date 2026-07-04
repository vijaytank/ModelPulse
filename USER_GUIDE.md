# 📖 ModelPulse User & Configuration Guide

This guide provides instructions on system prerequisites, installation paths, and configuration management for **ModelPulse**.

---

## 💻 System Prerequisites

Before installing ModelPulse, ensure that your system matches the following prerequisites:

*   **Operating System**: Windows 10 or Windows 11 (64-bit).
*   **Target Framework**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) or newer (specifically the WPF/Windows Desktop package).
*   **Hardware (GPU Telemetry)**:
    *   *NVIDIA*: NVML driver interface (`nvml.dll` installed with GeForce Experience or NVIDIA Drivers).
    *   *AMD/Intel/Generic*: DXGI (DirectX Graphics Infrastructure) support.
*   **Local Runtimes** (Optional but recommended for full usage):
    *   [Ollama](https://ollama.com) (v0.1.48 or newer).
    *   [llama.cpp](https://github.com/ggerganov/llama.cpp) (compiled with health & metrics server options).

---

## 📦 Installation Instructions

ModelPulse supports two quick installation methods:

### 1. PowerShell Script Installation (Recommended for Local Dev)

The PowerShell installation script automatically checks for admin rights, verifies prerequisites, downloads or builds binaries, configures directory paths, and creates default configuration profiles:

1.  Open a PowerShell terminal as **Administrator**.
2.  Run the installation command:
    ```powershell
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    irm -Uri "https://raw.githubusercontent.com/vijaytank/ModelPulse/main/Install-ModelPulse.ps1" | iex
    ```
3.  Once completed, you can start the monitor directly from your command line:
    ```powershell
    ModelPulse.UI.exe
    ```

### 2. Winget Package Manager

If ModelPulse is distributed as a Winget manifest, you can install it using the Windows Package Manager:

```powershell
winget install ModelPulse
```
*(Or install using the local package manifest definition):*
```powershell
winget install --manifest ./deployment/manifest.xml
```

---

## ⚙️ Configuration Guide

ModelPulse configuration is stored locally as a JSON document in `%APPDATA%\ModelPulse\settings.json`. Below is an overview of the schema keys and properties defined in `ConfigModels.cs` that govern the telemetry engine, alerts, and UI display:

| JSON Key | Type | Default | Safe Range / Valid Values | Description |
| :--- | :--- | :--- | :--- | :--- |
| `polling_mode` | `string` | `"default"` | `"default"`, `"low-power"`, `"custom"` | Controls active/idle polling frequencies. |
| `polling_intervals.idle_ms` | `integer` | `3000` | `500` – `60000` | Interval when no active generation/inference is detected. |
| `polling_intervals.active_ms` | `integer` | `1000` | `100` – `10000` | High-frequency interval while generation is active. |
| `runtimes.ollama.enabled` | `boolean` | `true` | `true`, `false` | Enable/Disable telemetry polling for Ollama runtime. |
| `runtimes.ollama.endpoint` | `string` | `"http://127.0.0.1:11434"` | Valid HTTP URL | Local address of the Ollama API service. |
| `runtimes.llama_cpp.enabled` | `boolean` | `false` | `true`, `false` | Enable/Disable telemetry polling for llama.cpp runtime. |
| `runtimes.llama_cpp.endpoint` | `string` | `"http://127.0.0.1:8080"` | Valid HTTP URL | Local address of the llama.cpp server instance. |
| `alerts.cooldown_seconds` | `integer` | `60` | `5` – `3600` | Minimum delay in seconds before repeating an alert of the same type. |
| `alerts.vram_warning_threshold_percent` | `double` | `90.0` | `0.0` – `100.0` | Threshold percentage of VRAM allocation before trigger. |
| `ui.always_on_top` | `boolean` | `true` | `true`, `false` | Keeps overlay panel floating on top of other desktop windows. |
| `ui.opacity` | `double` | `0.9` | `0.1` – `1.0` | Translucency level of the compact/expanded overlay windows. |
| `ui.launch_overlay_on_startup` | `boolean` | `false` | `true`, `false` | If true, opens overlay immediately on startup. If false, runs in tray. |

---

## 🔒 Runtime Environment Variable Overrides

For security, automation, or custom testing setups, you can override any configuration defaults at runtime using Environment Variables. When present, environment variables are loaded on startup and override persistent JSON settings:

| Environment Variable | Description |
| :--- | :--- |
| `MODELPULSE_POLLING_MODE` | Override polling profile (`default`, `low-power`, `custom`). |
| `MODELPULSE_POLLING_INTERVAL_IDLE_MS` | Override background/idle interval in milliseconds. |
| `MODELPULSE_POLLING_INTERVAL_ACTIVE_MS` | Override active inference interval in milliseconds. |
| `MODELPULSE_OLLAMA_ENABLED` | Set Ollama polling state (`true` / `false`). |
| `MODELPULSE_OLLAMA_ENDPOINT` | Set Ollama API server address. |
| `MODELPULSE_LLAMACPP_ENABLED` | Set llama.cpp polling state (`true` / `false`). |
| `MODELPULSE_LLAMACPP_ENDPOINT` | Set llama.cpp API server address. |
| `MODELPULSE_ALERT_COOLDOWN_SECONDS` | Override the alert repeat cooldown window. |
| `MODELPULSE_ALERT_VRAM_WARNING_THRESHOLD_PERCENT` | Set custom target warning threshold for VRAM pressure. |
| `MODELPULSE_UI_ALWAYS_ON_TOP` | Force overlay window to float on top. |
| `MODELPULSE_UI_OPACITY` | Set overlay opacity level (e.g. `0.85`). |
| `MODELPULSE_UI_LAUNCH_OVERLAY_ON_STARTUP` | Enforce overlay popup at launch (`true` / `false`). |

#### Example: Setting overrides in PowerShell before launching ModelPulse:
```powershell
$env:MODELPULSE_OLLAMA_ENDPOINT = "http://192.168.1.50:11434"
$env:MODELPULSE_ALERT_COOLDOWN_SECONDS = "120"
$env:MODELPULSE_UI_LAUNCH_OVERLAY_ON_STARTUP = "true"

# Launch ModelPulse - it will apply these settings immediately
ModelPulse.UI.exe
```
