using System.Runtime.InteropServices;

namespace Pusula.Startup;

/// <summary>
/// Keeping the console window open when pusula stops with an error and the window is its own: the downloaded file started with a
/// double click on Windows runs in a console that Explorer made for it, and that window closes with the program, before anything
/// in it can be read. Only on Windows: the Terminal of macOS keeps the window of a process that did not end cleanly, and on Linux
/// it depends on the terminal.
/// </summary>
internal static class ConsoleWindow
{
    /// <summary>The line that asks to press Enter.</summary>
    internal const string PauseLine = "Press Enter to close this window.";

    /// <summary>
    /// Whether to wait for Enter before the program ends: on Windows, when this process is the only one attached to its console (a
    /// double click; from a terminal the shell is attached too) and when standard input is not redirected (something has to be there
    /// to type Enter). Under <c>dotnet test</c> the console is shared or redirected, so tests never pause.
    /// </summary>
    /// <param name="isWindows">Whether this is Windows.</param>
    /// <param name="attachedProcessCount">How many processes are attached to the console.</param>
    /// <param name="inputRedirected">Whether standard input is redirected.</param>
    /// <param name="startedByExplorer">Whether the parent process is Explorer (a person's double click). A launcher that gives the
    /// program a console nobody sees (a hidden window, a scheduled task, a service wrapper) has another parent, and a program that
    /// waits there for Enter would never end.</param>
    public static bool ShouldPause(bool isWindows, int attachedProcessCount, bool inputRedirected, bool startedByExplorer) =>
        isWindows && attachedProcessCount == 1 && !inputRedirected && startedByExplorer;

    /// <summary>Whether to pause here and now, by the console of this process.</summary>
    public static bool ShouldPauseHere() =>
        OperatingSystem.IsWindows() && ShouldPause(isWindows: true, AttachedProcessCount(), Console.IsInputRedirected, StartedByExplorer());

    /// <summary>Writes the message of a mistake on the error stream, waits if the window is ours to keep, and gives the exit code 1.</summary>
    /// <param name="message">The one line to say.</param>
    public static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PauseIfOwned();
        return 1;
    }

    /// <summary>Prints an empty line and <see cref="PauseLine"/>, and waits for Enter, when <see cref="ShouldPauseHere"/> says so.</summary>
    public static void PauseIfOwned()
    {
        if (!ShouldPauseHere())
        {
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(PauseLine);
        Console.ReadLine();
    }

    /// <summary>
    /// Makes an exception that nothing handles stop the way a mistake does: it is printed, the window waits for Enter and the exit code
    /// is 1. Only when the window is ours to keep; otherwise the runtime ends the program as it always did.
    /// </summary>
    public static void GuardUnhandledExceptions()
    {
        if (!ShouldPauseHere())
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
        {
            Console.Error.WriteLine(e.ExceptionObject);
            PauseIfOwned();
            Environment.Exit(1);
        };
    }

    // The number of processes attached to the console, or 0 when it cannot be told (then there is no pause).
    private static int AttachedProcessCount()
    {
        try
        {
            // With room for one process the call returns how many there are when more are attached (and 0 when it fails).
            uint first = 0;
            return (int)GetConsoleProcessList(ref first, 1);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    // Whether the parent of this process is explorer.exe; false when it cannot be told (then there is no pause).
    private static bool StartedByExplorer()
    {
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            var information = default(ProcessBasicInformation);
            if (NtQueryInformationProcess(self.Handle, 0, ref information, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0)
            {
                return false;
            }

            using var parent = System.Diagnostics.Process.GetProcessById((int)information.InheritedFromUniqueProcessId);
            return string.Equals(parent.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ArgumentException
                                              or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(nint process, int informationClass, ref ProcessBasicInformation information, int length, out int returnLength);

    // DllImport and not LibraryImport: the latter needs unsafe code in the project, and both arguments here are plain numbers.
    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(ref uint processList, uint processCount);
}
