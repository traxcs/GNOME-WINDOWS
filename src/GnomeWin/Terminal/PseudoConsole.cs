using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GnomeWin.Terminal;

public sealed class PseudoConsole : IDisposable
{
    private IntPtr _hpc;
    private readonly SafeFileHandle _inputWrite, _outputRead;
    private readonly FileStream _input;
    private readonly Thread _reader;
    private readonly IntPtr _process, _thread;
    private readonly object _writeLock = new();
    private int _disposed;

    public event Action<string>? Output;
    public event Action<int>? Exited;

    public int ProcessId { get; }

    public PseudoConsole(string commandLine, string? workingDirectory, short columns, short rows)
    {
        if (!CreatePipe(out var inputRead, out _inputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        if (!CreatePipe(out _outputRead, out var outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();

        int hr = CreatePseudoConsole(new COORD { X = columns, Y = rows }, inputRead, outputWrite, 0, out _hpc);
        inputRead.Dispose();
        outputWrite.Dispose();
        if (hr != 0) throw new Win32Exception(hr, "CreatePseudoConsole failed");

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        si.lpAttributeList = Marshal.AllocHGlobal(size);
        try
        {
            if (!InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref size)) throw new Win32Exception();
            if (!UpdateProcThreadAttribute(si.lpAttributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();

            var cmd = new StringBuilder(commandLine);
            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                    IntPtr.Zero, string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory, ref si, out var pi))
                throw new Win32Exception();
            _process = pi.hProcess;
            _thread = pi.hThread;
            ProcessId = pi.dwProcessId;
        }
        finally
        {
            DeleteProcThreadAttributeList(si.lpAttributeList);
            Marshal.FreeHGlobal(si.lpAttributeList);
        }

        _input = new FileStream(_inputWrite, FileAccess.Write, 1);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "GnomeWin.Console.Reader" };
        _reader.Start();
    }

    private void ReadLoop()
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[16384];
        var chars = new char[16384 + 4];
        try
        {
            using var output = new FileStream(_outputRead, FileAccess.Read, 1);
            int n;
            while ((n = output.Read(bytes, 0, bytes.Length)) > 0)
            {
                int c = decoder.GetChars(bytes, 0, n, chars, 0);
                if (c > 0) Output?.Invoke(new string(chars, 0, c));
            }
        }
        catch (IOException) { /* pipe closed */ }
        catch (ObjectDisposedException) { }
        int code = 0;
        if (_process != IntPtr.Zero)
        {
            WaitForSingleObject(_process, 2000);
            GetExitCodeProcess(_process, out code);
        }
        Exited?.Invoke(code);
    }

    public void Write(string text)
    {
        if (_disposed != 0 || text.Length == 0) return;
        var data = Encoding.UTF8.GetBytes(text);
        lock (_writeLock)
        {
            try { _input.Write(data, 0, data.Length); _input.Flush(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public void Resize(int columns, int rows)
    {
        if (_disposed == 0 && _hpc != IntPtr.Zero && columns > 0 && rows > 0)
            ResizePseudoConsole(_hpc, new COORD { X = (short)columns, Y = (short)rows });
    }

    public bool HasExited => _process == IntPtr.Zero || WaitForSingleObject(_process, 0) == 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_hpc != IntPtr.Zero) { ClosePseudoConsole(_hpc); _hpc = IntPtr.Zero; }
        try { _input.Dispose(); } catch { }
        if (_process != IntPtr.Zero) CloseHandle(_process);
        if (_thread != IntPtr.Zero) CloseHandle(_thread);
    }

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [StructLayout(LayoutKind.Sequential)] private struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);
    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(COORD size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr hpc);
    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr hpc, COORD size);
    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hpc);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? app, StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string? dir, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out int code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
