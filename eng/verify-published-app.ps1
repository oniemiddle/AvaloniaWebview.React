#requires -Version 5.1
<#
.SYNOPSIS
    Launches a published ReactHost build from an isolated temp copy, proves the native
    WebView2 actually hosts content, captures a PNG of the app window and writes a JSON report.

.DESCRIPTION
    Verification harness for a NativeAOT (or any published) samples/ReactHost style build.
    It works only with PowerShell / Windows APIs that ship with the OS:

      1. Copies -AppDirectory into a fresh unique directory under %TEMP%\dsh-aot-verify-<guid>.
         Runtime WebView2 browser-profile folders (for example ReactHost.exe.WebView2) are
         intentionally not copied, so the launched copy always starts with an isolated profile
         and can never collide with another running instance.
      2. Starts <AppDirectory>\ReactHost.exe (or the only .exe found in the directory) from the copy.
      3. Waits up to -TimeoutSeconds for Process.MainWindowHandle != 0 after Refresh(),
         then records the window title.
      4. Counts descendant msedgewebview2.exe processes of the app PID via
         Get-CimInstance Win32_Process parent/child traversal. This is the proof that the native
         WebView2 control actually started and is hosting a browser.
      5. Captures the window to -ScreenshotPath with PrintWindow(..., PW_RENDERFULLCONTENT=2).
         If that capture is blank, it falls back to the window rectangle plus a screen copy
         (GetWindowRect + BitBlt from the desktop DC, the GDI equivalent of CopyFromScreen).
         The PNG is encoded by a small compiled helper (no System.Drawing dependency, so the
         script also runs on pwsh 7 where System.Drawing.Common is not part of the runtime).
      6. If Windows UI Automation is available (Windows PowerShell / .NET Framework builds),
         the visible Text elements are read and the sample status line is inspected for
         'React 已就绪'. The result is recorded in Notes; it does not change Success because the
         agreed Success contract is window + non-blank screenshot + WebView2 process.
      7. Always stops the whole process tree (descendants via WMI, plus taskkill /T /F as a
         backstop) and deletes the temp copy unless -KeepTemp is specified.
      8. Writes the JSON report and exits 0 on Success, 1 otherwise.

.PARAMETER AppDirectory
    Directory that contains the published ReactHost.exe (for example
    samples\ReactHost\bin\Release\net10.0-windows or an AOT publish output directory).

.PARAMETER ResultPath
    JSON result path. Defaults to <repo>\artifacts\aot-verify\published-app-result.json.

.PARAMETER ScreenshotPath
    PNG path. Defaults to <repo>\artifacts\aot-verify\published-app.png.

.PARAMETER TimeoutSeconds
    Maximum time to wait for the window, the WebView2 process tree and React readiness. Defaults to 45.

.PARAMETER ExpectedWindowTitle
    Window title that GetWindowText must return for the run to succeed. Defaults to the
    samples/ReactHost title 'Avalonia + React'. Pass an empty string to skip the title check.

.PARAMETER KeepTemp
    Keep the isolated temp copy for post-mortem inspection instead of deleting it.

.EXAMPLE
    pwsh -NoProfile -File eng\verify-published-app.ps1 -AppDirectory samples\ReactHost\bin\Release\net10.0-windows

.EXAMPLE
    pwsh -NoProfile -File eng\verify-published-app.ps1 -AppDirectory artifacts\publish\ReactHost-win-x64-aot -KeepTemp
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$AppDirectory,

    [string]$ResultPath,

    [string]$ScreenshotPath,

    [ValidateRange(5, 600)]
    [int]$TimeoutSeconds = 45,

    [string]$ExpectedWindowTitle = 'Avalonia + React',

    [switch]$KeepTemp
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Paths and state
# ---------------------------------------------------------------------------------------------
$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$repoRoot = Split-Path -Parent $scriptRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = (Get-Location).Path
}

function Resolve-FullPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

if ([string]::IsNullOrWhiteSpace($ResultPath)) {
    $ResultPath = Join-Path $repoRoot 'artifacts/aot-verify/published-app-result.json'
}
if ([string]::IsNullOrWhiteSpace($ScreenshotPath)) {
    $ScreenshotPath = Join-Path $repoRoot 'artifacts/aot-verify/published-app.png'
}

$resultFullPath = Resolve-FullPath $ResultPath
$screenshotFullPath = Resolve-FullPath $ScreenshotPath
$appFullPath = Resolve-FullPath $AppDirectory

$startedAt = Get-Date
$notes = [System.Collections.Generic.List[string]]::new()
$processStarted = $false
$windowFound = $false
$windowTitle = $null
$webview2Count = 0
$screenshotCaptured = $false
$screenshotNonBlank = $false
$reactReady = $null
$statusTexts = @()
$errorMessage = $null
$tempDir = $null
$appProcess = $null
$exeName = 'ReactHost.exe'
$captureHelperLoaded = $false
$uiaAvailable = $false
$success = $false
$processAlive = $false
$titleMatches = $false
$durationMilliseconds = 0

function Add-Note([string]$Text) {
    if (-not [string]::IsNullOrWhiteSpace($Text)) {
        $notes.Add($Text)
    }
}

# ---------------------------------------------------------------------------------------------
# Process / WMI helpers
# ---------------------------------------------------------------------------------------------
function Get-ProcessSnapshot {
    # Only the parent/child fields are needed. Do not request CommandLine: WMI marshals it
    # for every process and makes the snapshot several times slower.
    $snapshot = @()
    $hasCim = $null -ne (Get-Command Get-CimInstance -ErrorAction SilentlyContinue)
    if ($hasCim) {
        $snapshot = @(Get-CimInstance -ClassName Win32_Process -Property Name, ProcessId, ParentProcessId -ErrorAction Stop)
    }
    else {
        $snapshot = @(Get-WmiObject -Class Win32_Process -ErrorAction Stop |
            Select-Object Name, ProcessId, ParentProcessId)
    }
    return $snapshot
}

function Get-ProcessTree([int]$RootProcessId) {
    $snapshot = @(Get-ProcessSnapshot)
    $children = @{}
    foreach ($item in $snapshot) {
        $parentId = [int]$item.ParentProcessId
        if (-not $children.ContainsKey($parentId)) {
            $children[$parentId] = [System.Collections.Generic.List[int]]::new()
        }
        $children[$parentId].Add([int]$item.ProcessId)
    }

    $tree = [System.Collections.Generic.List[object]]::new()
    $queue = [System.Collections.Generic.Queue[int]]::new()
    $seen = @{}
    $queue.Enqueue($RootProcessId)
    $seen[$RootProcessId] = $true

    while ($queue.Count -gt 0) {
        $currentId = $queue.Dequeue()
        $node = $snapshot | Where-Object { [int]$_.ProcessId -eq $currentId } | Select-Object -First 1
        if ($null -ne $node) {
            $tree.Add($node)
        }
        if ($children.ContainsKey($currentId)) {
            foreach ($childId in $children[$currentId]) {
                if (-not $seen.ContainsKey($childId)) {
                    $seen[$childId] = $true
                    $queue.Enqueue($childId)
                }
            }
        }
    }
    return $tree
}

function Get-WebView2DescendantCount([int]$RootProcessId) {
    $count = 0
    foreach ($node in @(Get-ProcessTree -RootProcessId $RootProcessId)) {
        if ([string]::Equals([string]$node.Name, 'msedgewebview2.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
            $count++
        }
    }
    return $count
}

# ---------------------------------------------------------------------------------------------
# UI Automation helpers (Windows PowerShell only; silently skipped on pwsh 7)
# ---------------------------------------------------------------------------------------------
function Initialize-UiAutomation {
    try {
        Add-Type -AssemblyName UIAutomationClient -ErrorAction Stop
        Add-Type -AssemblyName UIAutomationTypes -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Get-WindowTextNames([IntPtr]$WindowHandle) {
    $names = [System.Collections.Generic.List[string]]::new()
    try {
        $rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($WindowHandle)
        if ($null -ne $rootElement) {
            $condition = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)
            $elements = $rootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
            for ($index = 0; $index -lt $elements.Count; $index++) {
                $name = $elements[$index].Current.Name
                if (-not [string]::IsNullOrWhiteSpace($name)) {
                    $names.Add($name)
                }
            }
        }
    }
    catch {
        # The window can go away or reject automation at any moment; treat as no data.
    }
    return $names
}

# ---------------------------------------------------------------------------------------------
# Window capture helper (P/Invoke + PNG encoding, no System.Drawing)
# ---------------------------------------------------------------------------------------------
function Initialize-CaptureHelper {
    $source = @'
using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

public sealed class PublishedAppCaptureResult
{
    public bool Success;
    public string Mode = "";
    public int Width;
    public int Height;
    public int DistinctColorBuckets;
    public double NonUniformRatio;
    public string Message = "";
}

public static class PublishedAppWindowCapture
{
    private const uint PW_RENDERFULLCONTENT = 2;
    private const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, uint rop);

    public static string GetWindowTitle(IntPtr hWnd)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    public static void PrepareForCapture(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }
        ShowWindow(hWnd, 9);
        SetForegroundWindow(hWnd);
        System.Threading.Thread.Sleep(1000);
    }

    public static PublishedAppCaptureResult Capture(IntPtr hWnd, string pngPath)
    {
        PublishedAppCaptureResult result = new PublishedAppCaptureResult();
        if (hWnd == IntPtr.Zero)
        {
            result.Message = "Window handle is zero; the app window is unavailable.";
            return result;
        }

        if (!IsWindowVisible(hWnd))
        {
            ShowWindow(hWnd, 9);
        }

        RECT rect;
        if (!GetWindowRect(hWnd, out rect))
        {
            result.Message = "GetWindowRect failed with Win32 error " + Marshal.GetLastWin32Error() + ".";
            return result;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 16 || height < 16 || width > 20000 || height > 20000)
        {
            result.Message = "Unreasonable window size " + width + "x" + height + ".";
            return result;
        }

        byte[] printed = null;
        int printedBuckets = 0;
        double printedRatio = 0.0;
        if (TryCapture(hWnd, width, height, true, out printed))
        {
            Analyze(printed, width, height, out printedBuckets, out printedRatio);
        }

        byte[] chosen = printed;
        result.Mode = "PrintWindow(PW_RENDERFULLCONTENT)";
        result.DistinctColorBuckets = printedBuckets;
        result.NonUniformRatio = printedRatio;

        bool needsFallback = printedBuckets < 8;
        if (needsFallback)
        {
            byte[] screen = null;
            int screenBuckets = 0;
            double screenRatio = 0.0;
            // The screen copy only shows the app when it is the foreground window.
            ShowWindow(hWnd, 9);
            SetForegroundWindow(hWnd);
            System.Threading.Thread.Sleep(900);
            if (TryCapture(hWnd, width, height, false, out screen))
            {
                Analyze(screen, width, height, out screenBuckets, out screenRatio);
            }
            if (screen != null && screenBuckets > printedBuckets)
            {
                chosen = screen;
                result.Mode = "CopyFromScreen";
                result.DistinctColorBuckets = screenBuckets;
                result.NonUniformRatio = screenRatio;
            }
        }

        if (chosen == null)
        {
            result.Message = "Both PrintWindow and the screen-copy fallback failed.";
            return result;
        }

        result.Width = width;
        result.Height = height;
        result.Success = true;
        if (result.DistinctColorBuckets < 8)
        {
            result.Message = "Captured image appears blank (" + result.DistinctColorBuckets + " distinct color buckets).";
        }
        SavePng(pngPath, chosen, width, height);
        return result;
    }

    private static bool TryCapture(IntPtr hWnd, int width, int height, bool usePrintWindow, out byte[] pixels)
    {
        pixels = null;
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero)
        {
            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
            return false;
        }

        BITMAPINFOHEADER header = new BITMAPINFOHEADER();
        header.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
        header.biWidth = width;
        header.biHeight = -height;
        header.biPlanes = 1;
        header.biBitCount = 32;
        header.biCompression = 0;

        IntPtr bits;
        IntPtr dib = CreateDIBSection(memoryDc, ref header, 0, out bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
            return false;
        }

        IntPtr previous = SelectObject(memoryDc, dib);
        bool captured = false;
        try
        {
            if (usePrintWindow)
            {
                captured = PrintWindow(hWnd, memoryDc, PW_RENDERFULLCONTENT);
            }
            else
            {
                RECT rect;
                if (screenDc != IntPtr.Zero && GetWindowRect(hWnd, out rect))
                {
                    captured = BitBlt(memoryDc, 0, 0, width, height, screenDc, rect.Left, rect.Top, SRCCOPY);
                }
            }

            if (captured)
            {
                byte[] buffer = new byte[width * height * 4];
                Marshal.Copy(bits, buffer, 0, buffer.Length);
                pixels = buffer;
            }
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                SelectObject(memoryDc, previous);
            }
            DeleteObject(dib);
            DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
        return captured;
    }

    private static void Analyze(byte[] pixels, int width, int height, out int distinctBuckets, out double nonUniformRatio)
    {
        bool[] seen = new bool[32768];
        distinctBuckets = 0;
        long total = (long)width * height;
        long differing = 0;
        byte firstB = pixels[0];
        byte firstG = pixels[1];
        byte firstR = pixels[2];
        for (long index = 0; index < total; index++)
        {
            int offset = (int)(index * 4);
            byte b = pixels[offset];
            byte g = pixels[offset + 1];
            byte r = pixels[offset + 2];
            int bucket = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
            if (!seen[bucket])
            {
                seen[bucket] = true;
                distinctBuckets++;
            }
            int delta = Math.Abs((int)r - (int)firstR) + Math.Abs((int)g - (int)firstG) + Math.Abs((int)b - (int)firstB);
            if (delta > 24)
            {
                differing++;
            }
        }
        nonUniformRatio = total == 0 ? 0.0 : (double)differing / (double)total;
    }

    private static void SavePng(string path, byte[] bgra, int width, int height)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        long rowLength = (long)width * 4 + 1;
        long rawLength = rowLength * height;
        if (rawLength > int.MaxValue)
        {
            throw new InvalidOperationException("Window is too large to encode as a PNG.");
        }

        byte[] raw = new byte[(int)rawLength];
        int position = 0;
        for (int y = 0; y < height; y++)
        {
            raw[position++] = 0;
            int source = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int offset = source + (x * 4);
                raw[position++] = bgra[offset + 2];
                raw[position++] = bgra[offset + 1];
                raw[position++] = bgra[offset];
                raw[position++] = 255;
            }
        }

        byte[] compressed = ZlibCompress(raw);
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            byte[] signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            stream.Write(signature, 0, signature.Length);

            byte[] header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8;
            header[9] = 6;
            header[10] = 0;
            header[11] = 0;
            header[12] = 0;
            WriteChunk(stream, "IHDR", header);
            WriteChunk(stream, "IDAT", compressed);
            WriteChunk(stream, "IEND", new byte[0]);
        }
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using (MemoryStream output = new MemoryStream())
        {
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (DeflateStream deflate = new DeflateStream(output, CompressionMode.Compress, true))
            {
                deflate.Write(data, 0, data.Length);
            }
            byte[] checksum = new byte[4];
            WriteBigEndian(checksum, 0, Adler32(data));
            output.Write(checksum, 0, 4);
            return output.ToArray();
        }
    }

    private static uint Adler32(byte[] data)
    {
        const uint Modulus = 65521;
        uint a = 1;
        uint b = 0;
        for (int index = 0; index < data.Length; index++)
        {
            a = (a + data[index]) % Modulus;
            b = (b + a) % Modulus;
        }
        return (b << 16) | a;
    }

    private static readonly uint[] CrcTable = CreateCrcTable();

    private static uint[] CreateCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                if ((c & 1) != 0)
                {
                    c = 0xEDB88320u ^ (c >> 1);
                }
                else
                {
                    c = c >> 1;
                }
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFFu;
        for (int index = 0; index < count; index++)
        {
            crc = CrcTable[(int)((crc ^ data[offset + index]) & 0xFF)] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length, 0, 4);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, 4);
        stream.Write(data, 0, data.Length);

        byte[] crcInput = new byte[4 + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, 4);
        if (data.Length > 0)
        {
            Buffer.BlockCopy(data, 0, crcInput, 4, data.Length);
        }
        byte[] crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, Crc32(crcInput, 0, crcInput.Length));
        stream.Write(crcBytes, 0, 4);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }
}
'@

    try {
        Add-Type -TypeDefinition $source -Language CSharp -ErrorAction Stop
        return $true
    }
    catch {
        $firstError = $_.Exception.Message
    }

    if ($PSVersionTable.PSEdition -eq 'Core') {
        try {
            Add-Type -TypeDefinition $source -Language CSharp -ErrorAction Stop -ReferencedAssemblies @(
                'System.IO.Compression', 'System.Runtime', 'System.Runtime.InteropServices')
            return $true
        }
        catch {
            Add-Note ('The window capture helper could not be compiled: ' + $_.Exception.Message)
            return $false
        }
    }

    Add-Note ('The window capture helper could not be compiled: ' + $firstError)
    return $false
}

# ---------------------------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------------------------
$screenshotPathValue = $null
$finalHandle = [IntPtr]::Zero
$cleanupMessages = [System.Collections.Generic.List[string]]::new()

try {
    if (-not (Test-Path -LiteralPath $appFullPath -PathType Container)) {
        throw ("AppDirectory '" + $appFullPath + "' does not exist or is not a directory.")
    }
    Add-Note ('App directory: ' + $appFullPath)

    $sourceExePath = Join-Path $appFullPath $exeName
    if (-not (Test-Path -LiteralPath $sourceExePath -PathType Leaf)) {
        $exeCandidates = @(Get-ChildItem -LiteralPath $appFullPath -Filter '*.exe' -File -ErrorAction SilentlyContinue)
        if ($exeCandidates.Count -eq 1) {
            $exeName = $exeCandidates[0].Name
            Add-Note ('ReactHost.exe was not present; using the only executable in the directory: ' + $exeName)
        }
        else {
            throw ("AppDirectory '" + $appFullPath + "' does not contain ReactHost.exe.")
        }
    }

    $captureHelperLoaded = Initialize-CaptureHelper
    if (-not $captureHelperLoaded) {
        Add-Note 'Window capture is unavailable because the capture helper did not compile; Success will be false.'
    }

    $uiaAvailable = Initialize-UiAutomation
    if (-not $uiaAvailable) {
        Add-Note 'Windows UI Automation is not available in this PowerShell edition (expected on pwsh 7); the status line is not inspected automatically.'
    }

    # Fresh, isolated working copy: copied first, exe launched from the copy only.
    $tempBase = $env:TEMP
    if ([string]::IsNullOrWhiteSpace($tempBase)) {
        $tempBase = [System.IO.Path]::GetTempPath()
    }
    $tempDir = Join-Path $tempBase ('dsh-aot-verify-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    Add-Note ('Isolated temp copy: ' + $tempDir)

    $copiedEntries = 0
    $skippedProfiles = 0
    foreach ($item in @(Get-ChildItem -LiteralPath $appFullPath -Force)) {
        if ($item.Name -like '*.WebView2') {
            $skippedProfiles++
            continue
        }
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $tempDir $item.Name) -Recurse -Force
        $copiedEntries++
    }
    Add-Note ('Copied ' + $copiedEntries + ' top-level entries into the temp copy.')
    if ($skippedProfiles -gt 0) {
        Add-Note ('Skipped ' + $skippedProfiles + ' runtime WebView2 profile folder(s) so the copy starts with a fresh browser profile.')
    }

    $tempExePath = Join-Path $tempDir $exeName
    if (-not (Test-Path -LiteralPath $tempExePath -PathType Leaf)) {
        throw ('The temp copy does not contain ' + $exeName + '.')
    }

    $appProcess = Start-Process -FilePath $tempExePath -WorkingDirectory $tempDir -PassThru
    $processStarted = $true
    Add-Note ('Started process ' + $appProcess.Id + ' from the temp copy.')

    # Wait for window, WebView2 processes and (when observable) React readiness.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $settleDeadline = $null
    $lastWebViewProbe = [datetime]::MinValue
    $lastUiaProbe = [datetime]::MinValue
    $webview2Count = 0
    $uiaTextsSeen = $false

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300

        if ($appProcess.HasExited) {
            Add-Note ('The process exited before verification completed (exit code ' + $appProcess.ExitCode + ').')
            break
        }

        $appProcess.Refresh()
        $handle = $appProcess.MainWindowHandle

        if (-not $windowFound -and $handle -ne [IntPtr]::Zero) {
            $windowFound = $true
            $windowTitle = $appProcess.MainWindowTitle
            $elapsedMs = [int]((Get-Date) - $startedAt).TotalMilliseconds
            Add-Note ('Main window detected after ' + $elapsedMs + ' ms: title=' + $windowTitle + ', handle=' + $handle.ToInt64() + '.')
        }

        if (-not $windowFound -or $handle -eq [IntPtr]::Zero) {
            continue
        }

        $now = Get-Date
        if (($now - $lastWebViewProbe).TotalSeconds -ge 1) {
            $webview2Count = [int](Get-WebView2DescendantCount -RootProcessId $appProcess.Id)
            $lastWebViewProbe = $now
        }

        if ($uiaAvailable -and ($now - $lastUiaProbe).TotalSeconds -ge 0.8) {
            $uiaTexts = @(Get-WindowTextNames -WindowHandle $handle)
            $lastUiaProbe = $now
            if ($uiaTexts.Count -gt 0) {
                $uiaTextsSeen = $true
                $statusTexts = $uiaTexts
                $hasReadyText = $false
                foreach ($text in $statusTexts) {
                    if ($text -like '*React 已就绪*') {
                        $hasReadyText = $true
                    }
                }
                if ($hasReadyText) {
                    $reactReady = $true
                }
                elseif ($null -eq $reactReady) {
                    $reactReady = $false
                }
            }
        }

        if ($webview2Count -ge 1 -and $null -eq $settleDeadline) {
            $settleDeadline = (Get-Date).AddSeconds(12)
            Add-Note ('Detected ' + $webview2Count + ' msedgewebview2.exe descendant process(es).')
        }

        if ($webview2Count -ge 1) {
            if ($uiaTextsSeen) {
                if ($reactReady -eq $true) {
                    Start-Sleep -Milliseconds 750
                    break
                }
            }
            elseif ($null -ne $settleDeadline -and (Get-Date) -ge $settleDeadline) {
                break
            }
        }
    }

    $appProcess.Refresh()
    $finalHandle = $appProcess.MainWindowHandle
    if ($finalHandle -eq [IntPtr]::Zero) {
        $windowFound = $false
    }
    else {
        $windowFound = $true
        if ([string]::IsNullOrWhiteSpace($windowTitle)) {
            $windowTitle = $appProcess.MainWindowTitle
        }
    }

    $processAlive = $false
    if (-not $appProcess.HasExited) {
        $processAlive = $true
        $webview2Count = [int](Get-WebView2DescendantCount -RootProcessId $appProcess.Id)
    }

    # Bring the app window to the foreground before capturing (ShowWindow(SW_RESTORE) +
    # SetForegroundWindow + settle) so the screen-copy fallback sees this window and not
    # whatever happened to be on top. Also refresh the title with GetWindowText.
    if ($finalHandle -ne [IntPtr]::Zero -and $captureHelperLoaded) {
        try {
            [PublishedAppWindowCapture]::PrepareForCapture($finalHandle)
            $liveTitle = [PublishedAppWindowCapture]::GetWindowTitle($finalHandle)
            if (-not [string]::IsNullOrWhiteSpace($liveTitle)) {
                $windowTitle = $liveTitle
            }
        }
        catch {
            Add-Note ('Window foreground/title preparation reported: ' + $_.Exception.Message)
        }
    }

    $titleMatches = $true
    if (-not [string]::IsNullOrWhiteSpace($ExpectedWindowTitle)) {
        $titleMatches = ($null -ne $windowTitle -and [string]::Equals([string]$windowTitle, [string]$ExpectedWindowTitle, [System.StringComparison]::Ordinal))
        if (-not $titleMatches) {
            Add-Note ('Window title mismatch: expected "' + $ExpectedWindowTitle + '" but GetWindowText returned "' + $windowTitle + '".')
        }
    }

    if ($uiaAvailable) {
        if ($uiaTextsSeen) {
            if ($reactReady -eq $true) {
                Add-Note 'React bridge readiness observed in the status line (React 已就绪).'
            }
            else {
                $statusLine = ($statusTexts -join ' | ')
                Add-Note ('React bridge readiness was NOT observed within ' + $TimeoutSeconds + ' seconds. Visible status line: ' + $statusLine)
            }
        }
        else {
            Add-Note 'UI Automation returned no text elements; React readiness could not be inspected.'
        }
    }

    # Screenshot of the app window.
    if ($windowFound -and $finalHandle -ne [IntPtr]::Zero -and $captureHelperLoaded) {
        $capture = [PublishedAppWindowCapture]::Capture($finalHandle, $screenshotFullPath)
        if ($null -ne $capture) {
            if ($capture.Success) {
                $screenshotCaptured = $true
                $screenshotPathValue = $screenshotFullPath
                $screenshotNonBlank = $capture.DistinctColorBuckets -ge 8
                Add-Note ('Screenshot mode: ' + $capture.Mode + ', ' + $capture.Width + 'x' + $capture.Height +
                    ', distinct color buckets=' + $capture.DistinctColorBuckets +
                    ', non-uniform ratio=' + [math]::Round($capture.NonUniformRatio, 4) + '.')
                if (-not $screenshotNonBlank) {
                    Add-Note ('Screenshot appears blank: ' + $capture.Message)
                }
            }
            else {
                Add-Note ('Screenshot capture failed: ' + $capture.Message)
            }
        }
        else {
            Add-Note 'Screenshot capture returned no result.'
        }
    }
    elseif (-not $captureHelperLoaded) {
        Add-Note 'Screenshot skipped because the capture helper failed to compile.'
    }
    else {
        Add-Note 'Screenshot skipped because the app window was not found.'
    }

    if ($reactReady -eq $true) {
        Add-Note 'React UI reported ready (bridge handshake observed).'
    }

    if (-not $processStarted) {
        $errorMessage = 'The application process did not start.'
    }
    elseif (-not $windowFound) {
        $errorMessage = 'The application main window was not found within ' + $TimeoutSeconds + ' seconds.'
    }
    elseif (-not $processAlive) {
        $errorMessage = 'The application process exited before verification finished.'
    }
    elseif (-not $titleMatches) {
        $errorMessage = 'The main window title did not match the expected title "' + $ExpectedWindowTitle + '" (actual: "' + $windowTitle + '").'
    }
    elseif ($webview2Count -lt 1) {
        $errorMessage = 'No msedgewebview2.exe descendant process was found; the native WebView2 control did not start.'
    }
    elseif (-not $screenshotNonBlank) {
        $errorMessage = 'The window screenshot was not captured or appears blank.'
    }
}
catch {
    $errorMessage = $_.Exception.Message
    Add-Note ('Harness error: ' + $_.Exception.Message)
}
finally {
    # Always stop the whole process tree, then remove the isolated copy.
    if ($null -ne $appProcess) {
        try {
            $tree = @(Get-ProcessTree -RootProcessId $appProcess.Id)
            $stopped = 0
            foreach ($node in $tree) {
                $nodeId = [int]$node.ProcessId
                try {
                    Stop-Process -Id $nodeId -Force -ErrorAction Stop
                    $stopped++
                }
                catch {
                }
            }
            if (-not $appProcess.HasExited) {
                $taskKill = Join-Path $env:SystemRoot 'System32\taskkill.exe'
                if (Test-Path -LiteralPath $taskKill) {
                    Start-Process -FilePath $taskKill -ArgumentList '/PID', $appProcess.Id, '/T', '/F' -WindowStyle Hidden -Wait -ErrorAction SilentlyContinue | Out-Null
                }
            }
            Start-Sleep -Milliseconds 500
            $cleanupMessages.Add('Stopped the app process tree (' + $stopped + ' process(es) signalled).')
        }
        catch {
            $cleanupMessages.Add('Process tree cleanup reported: ' + $_.Exception.Message)
        }
    }

    if ($null -ne $tempDir -and (Test-Path -LiteralPath $tempDir)) {
        $exeProcessName = [System.IO.Path]::GetFileNameWithoutExtension($exeName)
        foreach ($leftover in @(Get-Process -Name $exeProcessName -ErrorAction SilentlyContinue)) {
            try {
                if ($null -ne $leftover.Path -and $leftover.Path.StartsWith($tempDir, [System.StringComparison]::OrdinalIgnoreCase)) {
                    Stop-Process -Id $leftover.Id -Force -ErrorAction SilentlyContinue
                    $cleanupMessages.Add('Stopped leftover process ' + $leftover.Id + ' that was still running from the temp copy.')
                }
            }
            catch {
            }
        }
    }

    if ($null -ne $tempDir -and (Test-Path -LiteralPath $tempDir)) {
        if ($KeepTemp) {
            $cleanupMessages.Add('Temp copy kept because -KeepTemp was specified: ' + $tempDir)
        }
        else {
            $deleted = $false
            for ($attempt = 0; $attempt -lt 12; $attempt++) {
                Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
                if (-not (Test-Path -LiteralPath $tempDir)) {
                    $deleted = $true
                    break
                }
                Start-Sleep -Milliseconds 500
            }
            if ($deleted) {
                $cleanupMessages.Add('Deleted the temp copy.')
            }
            else {
                $cleanupMessages.Add('Could not delete the temp copy (files still locked): ' + $tempDir)
            }
        }
    }

    foreach ($message in $cleanupMessages) {
        Add-Note $message
    }

    $success = ($processStarted -and $windowFound -and $processAlive -and $titleMatches -and ($webview2Count -ge 1) -and $screenshotNonBlank)
    $durationMilliseconds = [int]((Get-Date) - $startedAt).TotalMilliseconds

    $resultObject = [ordered]@{
        Success              = [bool]$success
        ProcessStarted       = [bool]$processStarted
        WindowFound          = [bool]$windowFound
        WindowTitle          = $windowTitle
        WebView2ProcessCount = [int]$webview2Count
        ScreenshotPath       = $screenshotPathValue
        DurationMilliseconds = [int]$durationMilliseconds
        Error                = $errorMessage
        Notes                = @($notes)
    }

    $json = $resultObject | ConvertTo-Json -Depth 5
    try {
        $resultDirectory = Split-Path -Parent $resultFullPath
        if (-not [string]::IsNullOrWhiteSpace($resultDirectory)) {
            New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
        }
        [System.IO.File]::WriteAllText($resultFullPath, $json, (New-Object System.Text.UTF8Encoding($false)))
    }
    catch {
        Write-Output ('WARNING: could not write the JSON result to ' + $resultFullPath + ': ' + $_.Exception.Message)
    }

    $reactReadyText = 'unknown (UI Automation unavailable)'
    if ($uiaAvailable) {
        if ($reactReady -eq $true) {
            $reactReadyText = 'yes (status line shows React 已就绪)'
        }
        elseif ($uiaTextsSeen) {
            $reactReadyText = 'NO (status line: ' + ($statusTexts -join ' | ') + ')'
        }
        else {
            $reactReadyText = 'unknown (no UI Automation text elements)'
        }
    }

    $screenshotText = 'not captured'
    if ($screenshotCaptured) {
        $screenshotText = $screenshotFullPath
        if (-not $screenshotNonBlank) {
            $screenshotText += ' (blank)'
        }
    }

    Write-Output ''
    Write-Output '=== Published app verification ==='
    Write-Output ('App directory    : ' + $appFullPath)
    Write-Output ('Result JSON      : ' + $resultFullPath)
    Write-Output ('Process started  : ' + $processStarted)
    if ($null -ne $appProcess) {
        Write-Output ('App process ID   : ' + $appProcess.Id)
    }
    Write-Output ('Window found     : ' + $windowFound)
    Write-Output ('Window title     : ' + $windowTitle)
    Write-Output ('Title matches    : ' + $titleMatches + ' (expected: ' + $ExpectedWindowTitle + ')')
    Write-Output ('Process alive    : ' + $processAlive)
    Write-Output ('WebView2 procs   : ' + $webview2Count)
    Write-Output ('Screenshot       : ' + $screenshotText)
    Write-Output ('React ready      : ' + $reactReadyText)
    Write-Output ('Duration         : ' + $durationMilliseconds + ' ms')
    if (-not [string]::IsNullOrWhiteSpace($errorMessage)) {
        Write-Output ('Error            : ' + $errorMessage)
    }
    if ($success) {
        Write-Output 'RESULT: SUCCESS'
    }
    else {
        Write-Output 'RESULT: FAILURE'
    }
}

if ($success) {
    exit 0
}
exit 1
