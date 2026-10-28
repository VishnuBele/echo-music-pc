using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EchoMusic.Core;

/// <summary>
/// Protects small per-user session secrets with Windows DPAPI. Data is scoped to the
/// current Windows user (CRYPTPROTECT_LOCAL_MACHINE is deliberately not used).
/// </summary>
internal static class WindowsUserDataProtection
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("Crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Protect(byte[] clearBytes)
    {
        ArgumentNullException.ThrowIfNull(clearBytes);
        return Transform(clearBytes, protect: true);
    }

    public static byte[] Unprotect(byte[] protectedBytes)
    {
        ArgumentNullException.ThrowIfNull(protectedBytes);
        return Transform(protectedBytes, protect: false);
    }

    private static byte[] Transform(byte[] inputBytes, bool protect)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows DPAPI is available only on Windows.");

        IntPtr inputMemory = Marshal.AllocHGlobal(Math.Max(1, inputBytes.Length));
        if (inputBytes.Length > 0)
            Marshal.Copy(inputBytes, 0, inputMemory, inputBytes.Length);

        var input = new DataBlob { Length = inputBytes.Length, Data = inputMemory };
        DataBlob output = default;
        try
        {
            bool ok = protect
                ? CryptProtectData(ref input, "Echo Music session", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error(), protect ? "Could not protect the Echo Music session with Windows DPAPI." : "Could not decrypt the Echo Music session with Windows DPAPI.");

            var result = new byte[output.Length];
            if (output.Length > 0)
                Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(inputMemory);
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }
}
