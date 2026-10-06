param([Parameter(Mandatory=$true)][string]$Installer,[Parameter(Mandatory=$true)][string]$Catalog)
$ErrorActionPreference='Stop'
$record=(Get-Content $Catalog -Raw | ConvertFrom-Json).artifacts | Where-Object platform -eq 'windows-x64'
if ((Get-FileHash $Installer -Algorithm SHA256).Hash.ToLower() -ne $record.sha256 -or (Get-Item $Installer).Length -ne $record.size) { throw 'Installer identity mismatch' }
Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class InstallerUI {
 public delegate bool Callback(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h,Callback cb,IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public static string Text(IntPtr h){var s=new StringBuilder(2048);GetWindowText(h,s,s.Capacity);return s.ToString();}
 public static IntPtr Window(int pid){IntPtr found=IntPtr.Zero;EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid&&Text(h).Contains("Spacewars"))found=h;return true;},IntPtr.Zero);return found;}
 public static IntPtr Child(IntPtr parent,string text){IntPtr found=IntPtr.Zero;EnumChildWindows(parent,(h,l)=>{if(Text(h).Contains(text))found=h;return true;},IntPtr.Zero);return found;}
 public static string Children(IntPtr parent){var s=new StringBuilder();EnumChildWindows(parent,(h,l)=>{s.AppendLine(Text(h));return true;},IntPtr.Zero);return s.ToString();}
 public static void Click(IntPtr h){SendMessage(h,0xF5,IntPtr.Zero,IntPtr.Zero);}
 public static bool Checked(IntPtr h){return SendMessage(h,0xF0,IntPtr.Zero,IntPtr.Zero).ToInt32()==1;}
}
'@
$proc=Start-Process $Installer -PassThru
$deadline=(Get-Date).AddMinutes(5);$clicked=$false;$window=[IntPtr]::Zero;$run=[IntPtr]::Zero;$desktop=[IntPtr]::Zero
while ((Get-Date) -lt $deadline) {
 $window=[InstallerUI]::Window($proc.Id)
 if ($window -ne [IntPtr]::Zero) {
  $run=[InstallerUI]::Child($window,'Запустить Spacewars')
  $desktop=[InstallerUI]::Child($window,'Создать ярлык')
  if ($run -ne [IntPtr]::Zero -and $desktop -ne [IntPtr]::Zero) {break}
  if (-not $clicked) {
   $next=[InstallerUI]::Child($window,'Далее')
   if ($next -ne [IntPtr]::Zero) {[InstallerUI]::Click($next);$clicked=$true}
  }
 }
 Start-Sleep -Milliseconds 500
}
if ($run -eq [IntPtr]::Zero -or $desktop -eq [IntPtr]::Zero) { throw "Final screen missing: $([InstallerUI]::Children($window))" }
if (-not [InstallerUI]::Checked($run) -or -not [InstallerUI]::Checked($desktop)) {throw 'Both final checkboxes must default checked'}
if (-not [InstallerUI]::Text($window).Contains($record.version)) {throw 'Wizard title lacks version'}
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
try {
 $bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
 $bitmap=New-Object System.Drawing.Bitmap $bounds.Width,$bounds.Height
 $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
 $graphics.CopyFromScreen($bounds.Location,[System.Drawing.Point]::Empty,$bounds.Size)
 $bitmap.Save((Join-Path $PWD 'artifacts/final-screen.png'));$graphics.Dispose();$bitmap.Dispose()
} catch { Write-Warning "Screen capture unavailable: $_" }
# The launch default is verified; avoid opening a Unity game on a CI service desktop.
[InstallerUI]::Click($run)
$finish=[InstallerUI]::Child($window,'Готово')
if ($finish -eq [IntPtr]::Zero) {$finish=[InstallerUI]::Child($window,'Завершить')}
if ($finish -eq [IntPtr]::Zero) {throw 'Finish button missing'}
[InstallerUI]::Click($finish)
if (-not $proc.WaitForExit(30000) -or $proc.ExitCode -ne 0) {throw 'Installer did not complete'}
$install=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Spacewars').DisplayIcon | Split-Path
$expected=Join-Path $env:ProgramFiles 'Spacewars'
if ($install -ne $expected) {throw "Unexpected default directory: $install"}
$link=Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Spacewars.lnk'
if (-not (Test-Path $link)) {throw 'Public Desktop shortcut absent'}
$shortcut=(New-Object -ComObject WScript.Shell).CreateShortcut($link)
if ($shortcut.TargetPath -ne (Join-Path $install 'Spacewars.exe') -or $shortcut.IconLocation -notlike '*Spacewars.ico*') {throw 'Shortcut target/icon invalid'}
$before=Get-Content (Join-Path $install 'active.json') -Raw
$reinstall=Start-Process $Installer -ArgumentList '/S' -Wait -PassThru
if ($reinstall.ExitCode -ne 0 -or (Get-Content (Join-Path $install 'active.json') -Raw) -ne $before) {throw 'Reinstall changed valid active pointer'}
@{version=$record.version;sha256=$record.sha256;defaultDirectory=$install;shortcut=$link;target=$shortcut.TargetPath;icon=$shortcut.IconLocation;finalRunChecked=$true;finalShortcutChecked=$true;reinstall='passed'} | ConvertTo-Json | Set-Content artifacts/windows-readback.json
$uninstall=Start-Process (Join-Path $install 'Uninstall.exe') -ArgumentList '/S' -Wait -PassThru
Start-Sleep -Seconds 3
if (Test-Path $link) {throw 'Uninstall left desktop shortcut'}
