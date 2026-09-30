param([string]$Executable = $env:FIRAW_H4K_TARGET_EXE)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
try {
    if ([string]::IsNullOrWhiteSpace($Executable) -or -not [IO.Path]::IsPathRooted($Executable)) {
        throw 'An absolute executable path is required.'
    }
    $Executable = [IO.Path]::GetFullPath($Executable)
    if ([IO.Path]::GetFileName($Executable) -ne 'Firaw-h4K.exe') { throw 'Unexpected executable.' }
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { exit 0 }
    # Windows Restart Manager identifies users of this exact file (including watchdogs).
    # No process-name matching, RmShutdown, RmRestart, or process termination.
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class H4KRunningCheck {
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmStartSession(out uint handle, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint handle, uint fileCount, string[] files,
        uint processCount, IntPtr processes, uint serviceCount, IntPtr services);
    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint handle, out uint needed, ref uint count, IntPtr processes, ref uint reasons);
    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint handle);
    public static bool IsBusy(string path) {
        uint handle;
        int result = RmStartSession(out handle, 0, new StringBuilder(33));
        if (result != 0) throw new Win32Exception(result);
        try {
            result = RmRegisterResources(handle, 1, new string[] { path }, 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (result != 0) throw new Win32Exception(result);
            uint needed, count = 0, reasons = 0;
            result = RmGetList(handle, out needed, ref count, IntPtr.Zero, ref reasons);
            if (result == 234) return true; // ERROR_MORE_DATA: one or more file users.
            if (result != 0) throw new Win32Exception(result);
            return needed != 0 || count != 0;
        } finally { RmEndSession(handle); }
    }
}
'@
    if ([H4KRunningCheck]::IsBusy($Executable)) {
        Write-Output 'Firaw h4K is in use. Press Ctrl+Alt+F12, then close the app and retry.'
        exit 1618
    }
    exit 0
} catch {
    Write-Output "Cannot safely check the installed app: $($_.Exception.Message)"
    exit 1603
}
