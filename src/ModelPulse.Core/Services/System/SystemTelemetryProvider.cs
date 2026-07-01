using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using ModelPulse.Core.Models;

namespace ModelPulse.Core.Services.System
{
    /// <summary>
    /// Resolves GPU telemetry through a multi-provider fallback chain:
    /// NVML (NVIDIA) → DXGI (DirectX) → WMI → Unavailable.
    ///
    /// Also provides CPU, RAM, and widget self-telemetry.
    /// Per the architecture spec, GPU metrics that cannot be queried are
    /// reported as "Unavailable" rather than misleading zero values.
    /// </summary>
    public class SystemTelemetryProvider
    {
        // ─── Win32 DLL Loading ─────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        // ─── NVML Delegates ─────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory { public ulong total, free, used; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization { public uint gpu, memory; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlInit_v2();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlShutdown();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlDeviceGetName(IntPtr device, byte[] name, uint length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temp);

        // ─── DXGI COM Interfaces ─────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        public struct DxgiQueryVideoMemoryInfo
        {
            public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation;
        }

        [ComImport, Guid("770cae27-039f-4740-90c0-76ade55e7746"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
            [PreserveSig] int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
            [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
            [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
            [PreserveSig] int EnumAdapters1(uint Adapter, out IntPtr ppAdapter);
            [PreserveSig] int IsCurrent(out bool pCurrent);
        }

        [ComImport, Guid("64596741-4192-4fa6-bf2e-772441013409"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter3
        {
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
            [PreserveSig] int EnumOutputs(uint Output, out IntPtr ppOutput);
            [PreserveSig] int GetDesc(IntPtr pDesc);
            [PreserveSig] int CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);
            [PreserveSig] int GetDesc1(IntPtr pDesc);
            [PreserveSig] int GetDesc2(IntPtr pDesc);
            [PreserveSig] int RegisterHardwareContentProtectionTeardownStatusEvent(IntPtr hEvent, out uint pdwCookie);
            [PreserveSig] int UnregisterHardwareContentProtectionTeardownStatusEvent(uint dwCookie);
            [PreserveSig] int QueryVideoMemoryInfo(uint NodeIndex, uint MemorySegmentGroup, out DxgiQueryVideoMemoryInfo pVideoMemoryInfo);
        }

        [DllImport("dxgi.dll", SetLastError = true)]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 ppFactory);

        // ─────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves GPU telemetry using the NVML → DXGI → WMI fallback chain.
        /// Returns a state with SourcePath = "Unavailable" if all providers fail.
        /// Never throws.
        /// </summary>
        public virtual GpuTelemetryState ProbeGpu()
        {
            return TryNvml()
                ?? TryDxgi()
                ?? TryWmi()
                ?? new GpuTelemetryState { SourcePath = "Unavailable" };
        }

        /// <summary>
        /// Collects CPU, RAM, GPU, and widget process self-telemetry.
        /// </summary>
        public virtual SystemTelemetryState GetSystemState()
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();

            var state = new SystemTelemetryState
            {
                CpuPercent = GetCpuPercent(),
                RamUsedMb = GetRamUsedMb(),
                RamTotalMb = GetRamTotalMb(),
                WidgetRamMb = process.WorkingSet64 / (1024.0 * 1024.0),
                Gpu = ProbeGpu()
            };

            return state;
        }

        // ─────────────────────────────────────────────────────────────────
        // Private: System Metrics
        // ─────────────────────────────────────────────────────────────────

        private double GetCpuPercent()
        {
            try
            {
                // Use WMI for a single accurate point-in-time CPU load
                using var searcher = new ManagementObjectSearcher(
                    "SELECT LoadPercentage FROM Win32_Processor");
                double total = 0;
                int count = 0;
                foreach (var obj in searcher.Get())
                {
                    var load = obj["LoadPercentage"];
                    if (load != null && double.TryParse(load.ToString(), out var val))
                    {
                        total += val;
                        count++;
                    }
                }
                return count > 0 ? total / count : 0;
            }
            catch
            {
                return 0;
            }
        }

        private double GetRamUsedMb()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    var free = Convert.ToDouble(obj["FreePhysicalMemory"]);  // KB
                    var total = Convert.ToDouble(obj["TotalVisibleMemorySize"]);  // KB
                    return (total - free) / 1024.0; // to MB
                }
            }
            catch { }
            return 0;
        }

        private double GetRamTotalMb()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    return Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024.0;
                }
            }
            catch { }
            return 0;
        }

        // ─────────────────────────────────────────────────────────────────
        // Private: GPU Providers
        // ─────────────────────────────────────────────────────────────────

        private GpuTelemetryState? TryNvml()
        {
            string[] searchPaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"NVIDIA Corporation\NVSMI\nvml.dll")
            };

            IntPtr hModule = IntPtr.Zero;
            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    hModule = LoadLibrary(path);
                    if (hModule != IntPtr.Zero) break;
                }
            }

            if (hModule == IntPtr.Zero) return null;

            try
            {
                var init     = GetProc<NvmlInit_v2>(hModule, "nvmlInit_v2");
                var shutdown = GetProc<NvmlShutdown>(hModule, "nvmlShutdown");
                var getHandle = GetProc<NvmlDeviceGetHandleByIndex_v2>(hModule, "nvmlDeviceGetHandleByIndex_v2");
                var getMem   = GetProc<NvmlDeviceGetMemoryInfo>(hModule, "nvmlDeviceGetMemoryInfo");
                var getUtil  = GetProc<NvmlDeviceGetUtilizationRates>(hModule, "nvmlDeviceGetUtilizationRates");
                var getName  = GetProc<NvmlDeviceGetName>(hModule, "nvmlDeviceGetName");
                var getTemp  = GetProc<NvmlDeviceGetTemperature>(hModule, "nvmlDeviceGetTemperature");

                if (init() != 0) return null;

                try
                {
                    if (getHandle(0, out IntPtr device) != 0) return null;

                    var state = new GpuTelemetryState { SourcePath = "NVML" };

                    var nameBytes = new byte[64];
                    if (getName(device, nameBytes, (uint)nameBytes.Length) == 0)
                        state.Name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                    if (getMem(device, out var mem) == 0)
                    {
                        state.VramUsedMb  = mem.used  / (1024.0 * 1024.0);
                        state.VramTotalMb = mem.total / (1024.0 * 1024.0);
                    }

                    if (getUtil(device, out var util) == 0)
                        state.UtilizationPercent = util.gpu;

                    if (getTemp(device, 0, out uint temp) == 0)
                        state.TemperatureC = temp;

                    return state;
                }
                finally
                {
                    shutdown();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemTelemetryProvider/NVML] {ex.Message}");
                return null;
            }
            finally
            {
                FreeLibrary(hModule);
            }
        }

        private GpuTelemetryState? TryDxgi()
        {
            try
            {
                var factoryGuid = new Guid("770cae27-039f-4740-90c0-76ade55e7746");
                if (CreateDXGIFactory1(ref factoryGuid, out var factory) != 0) return null;

                uint i = 0;
                while (factory.EnumAdapters1(i, out IntPtr adapterPtr) == 0)
                {
                    try
                    {
                        var adapter = (IDXGIAdapter3)Marshal.GetObjectForIUnknown(adapterPtr);
                        if (adapter.QueryVideoMemoryInfo(0, 0, out var mem) == 0)
                        {
                            return new GpuTelemetryState
                            {
                                SourcePath    = "DXGI",
                                Name          = "Generic DXGI GPU",
                                VramUsedMb    = mem.CurrentUsage / (1024.0 * 1024.0),
                                VramTotalMb   = mem.Budget       / (1024.0 * 1024.0)
                            };
                        }
                    }
                    catch { }
                    finally { Marshal.Release(adapterPtr); }
                    i++;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemTelemetryProvider/DXGI] {ex.Message}");
            }
            return null;
        }

        private GpuTelemetryState? TryWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        double totalMb = 0;
                        if (double.TryParse(obj["AdapterRAM"]?.ToString(), out double bytes))
                            totalMb = bytes / (1024.0 * 1024.0);

                        return new GpuTelemetryState
                        {
                            Name          = name,
                            SourcePath    = "WMI",
                            VramTotalMb   = totalMb
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemTelemetryProvider/WMI] {ex.Message}");
            }
            return null;
        }

        private static T GetProc<T>(IntPtr hModule, string procName) where T : Delegate
        {
            var ptr = GetProcAddress(hModule, procName);
            if (ptr == IntPtr.Zero)
                throw new EntryPointNotFoundException($"Cannot find {procName} in nvml.dll");
            return (T)Marshal.GetDelegateForFunctionPointer(ptr, typeof(T));
        }
    }
}
