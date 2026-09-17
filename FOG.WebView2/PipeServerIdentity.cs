using Microsoft.Win32.SafeHandles;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace FOG.WebView2;

internal static class PipeServerIdentity
{
    internal static void Verify(NamedPipeClientStream pipe, int expectedProcessId)
    {
        if (expectedProcessId <= 0 ||
            !GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actualProcessId) ||
            actualProcessId != (uint)expectedProcessId)
        {
            throw new InvalidDataException("Named pipe server does not match the launched Agent.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
