using System;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Marmelad.Core;

public static class MemoryTrimmer
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    /// <summary>
    /// Gives memory back before a long wait in the tray. Nothing would trigger a collection
    /// there, so without this the leftovers of the last capture would stay for hours.
    /// </summary>
    public static void Trim()
    {
        // WPF images are freed by finalizers, so only the second pass really reclaims them.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        EmptyWorkingSet(GetCurrentProcess());
    }
}
