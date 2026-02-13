#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PhantomRender.ImGui.Native
{
    internal static class ConsoleFileLog
    {
        private static bool _installed;

        // Keep these alive for the lifetime of the process.
        private static TextWriter? _originalOut;
        private static TextWriter? _originalError;
        private static StreamWriter? _fileWriter;

        public static void Install(IntPtr hModule)
        {
            if (_installed) return;
            _installed = true;

            try
            {
                string logPath = GetLogPath(hModule);

                string? dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                // Share ReadWrite so we can open the log while the game is running.
                var fileStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _fileWriter = new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true
                };

                _fileWriter.WriteLine();
                _fileWriter.WriteLine($"===== PhantomRender session {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
                _fileWriter.WriteLine($"Process: {Process.GetCurrentProcess().ProcessName} (PID {Environment.ProcessId})");
                _fileWriter.WriteLine();

                _originalOut = Console.Out;
                _originalError = Console.Error;

                Console.SetOut(new TeeTextWriter(_originalOut, _fileWriter));
                Console.SetError(new TeeTextWriter(_originalError, _fileWriter));

                Console.WriteLine($"[PhantomRender] Log file: {logPath}");
            }
            catch (Exception ex)
            {
                // Best-effort: if file logging fails, keep console logging alive.
                try { Console.WriteLine($"[PhantomRender] Failed to initialize file logging: {ex.Message}"); } catch { }
            }
        }

        private static string GetLogPath(IntPtr hModule)
        {
            string processName = "game";
            try
            {
                string pn = Process.GetCurrentProcess().ProcessName;
                if (!string.IsNullOrWhiteSpace(pn))
                    processName = pn;
            }
            catch
            {
                // Ignore.
            }

            // Preferred: write next to the injected DLL so it's easy to find.
            try
            {
                string? modulePath = TryGetModuleFilePath(hModule);
                if (!string.IsNullOrWhiteSpace(modulePath))
                {
                    string? moduleDir = Path.GetDirectoryName(modulePath);
                    if (!string.IsNullOrWhiteSpace(moduleDir))
                        return Path.Combine(moduleDir, $"{processName}.log");
                }
            }
            catch
            {
                // Ignore and fall back.
            }

            // Fallback: LocalAppData\PhantomRender\Logs\<game>.log
            string fallbackDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhantomRender",
                "Logs");
            return Path.Combine(fallbackDir, $"{processName}.log");
        }

        private static unsafe string? TryGetModuleFilePath(IntPtr hModule)
        {
            const int MAX_PATH = 260;
            char* buffer = stackalloc char[MAX_PATH];
            uint len = GetModuleFileName(hModule, buffer, MAX_PATH);
            if (len == 0 || len >= MAX_PATH)
                return null;

            return new string(buffer, 0, (int)len);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern unsafe uint GetModuleFileName(IntPtr hModule, char* lpFilename, uint nSize);

        private sealed class TeeTextWriter : TextWriter
        {
            private readonly TextWriter _a;
            private readonly TextWriter _b;
            private readonly object _gate = new();

            public TeeTextWriter(TextWriter a, TextWriter b)
            {
                _a = a;
                _b = b;
            }

            public override Encoding Encoding => _a.Encoding;

            public override void Write(char value)
            {
                lock (_gate)
                {
                    try { _a.Write(value); } catch { }
                    try { _b.Write(value); } catch { }
                }
            }

            public override void Write(string? value)
            {
                lock (_gate)
                {
                    try { _a.Write(value); } catch { }
                    try { _b.Write(value); } catch { }
                }
            }

            public override void WriteLine(string? value)
            {
                lock (_gate)
                {
                    try { _a.WriteLine(value); } catch { }
                    try { _b.WriteLine(value); } catch { }
                }
            }

            public override void Flush()
            {
                lock (_gate)
                {
                    try { _a.Flush(); } catch { }
                    try { _b.Flush(); } catch { }
                }
            }
        }
    }
}
