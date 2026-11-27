using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FOG.Protocol;

[SupportedOSPlatform("windows")]
public sealed class FileReadLease : IDisposable
{
    private List<IDisposable>? _handles = new();

    public static FileReadLease LockDirectories(params string[] paths)
    {
        var lease = new FileReadLease();
        try
        {
            // Parents are pinned first so a child path cannot be redirected by renaming an ancestor.
            foreach (var path in paths.SelectMany(Ancestors).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path.Length))
            {
                var handle = CreateFileW(path, 0, FileShare.Read | FileShare.Write, IntPtr.Zero,
                    FileMode.Open, 0x02000000 | 0x00200000, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new IOException("Cannot lock trusted directory.", new Win32Exception(error));
                }
                lease._handles!.Add(handle);
                RejectReparsePoint(path);
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public FileStream OpenRead(string path)
    {
        ObjectDisposedException.ThrowIf(_handles is null, this);
        RejectReparsePoint(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        _handles!.Add(stream);
        RejectReparsePoint(path);
        return stream;
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            yield return directory.FullName;
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Trusted paths must not contain reparse points.");
    }

    public void Dispose()
    {
        var handles = Interlocked.Exchange(ref _handles, null);
        if (handles is null) return;
        for (var index = handles.Count - 1; index >= 0; index--) handles[index].Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share,
        IntPtr security, FileMode creation, uint flags, IntPtr template);
}
