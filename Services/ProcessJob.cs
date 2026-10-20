using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BlazorStoc.Services;

// A Windows Job Object around one worker process: a limit on the memory the process commits (managed AND native: OpenCV, Tesseract, PDFium, which the
// DOTNET_GCHeapHardLimit of the runtime does not cover) and "kill on close", so a worker never outlives the application that started it. Windows only; on
// any other system, or when the job cannot be created, the worker simply runs without it (Attach returns false and says nothing else).
public sealed class ProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint LimitProcessMemory = 0x00000100;
    private const uint LimitKillOnJobClose = 0x00002000;

    private IntPtr handle;

    private ProcessJob(IntPtr handle) => this.handle = handle;

    // The job, or null when jobs are not available here or the limits could not be set.
    public static ProcessJob? Create(long processMemoryLimitBytes)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return null;
        var information = new ExtendedLimitInformation();
        information.Basic.LimitFlags = LimitKillOnJobClose;
        if (processMemoryLimitBytes > 0)
        {
            information.Basic.LimitFlags |= LimitProcessMemory;
            information.ProcessMemoryLimit = (UIntPtr)(ulong)processMemoryLimitBytes;
        }
        var size = Marshal.SizeOf<ExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(information, buffer, false);
            if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size)) return new ProcessJob(job);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        CloseHandle(job);
        return null;
    }

    // Puts a started process into the job (its children follow it). False when the process already ended or cannot be assigned.
    public bool Attach(Process process)
    {
        if (handle == IntPtr.Zero) return false;
        try { return AssignProcessToJobObject(handle, process.Handle); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { return false; }
    }

    // The most memory one process of the job has committed so far (works after the process ended), or 0 when it cannot be read.
    public long PeakProcessMemoryBytes()
    {
        if (handle == IntPtr.Zero) return 0;
        var size = Marshal.SizeOf<ExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)size, IntPtr.Zero)) return 0;
            return (long)Marshal.PtrToStructure<ExtendedLimitInformation>(buffer).PeakProcessMemoryUsed;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // Closing the handle ends every process still in the job.
    public void Dispose()
    {
        var closing = Interlocked.Exchange(ref handle, IntPtr.Zero);
        if (closing != IntPtr.Zero) CloseHandle(closing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
