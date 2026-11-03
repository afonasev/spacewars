# Signed-inventory native view. Only fixed actions leave this process.
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object System.Windows.Forms.Form
$form.Text = 'Spacewars'
$form.Icon = New-Object System.Drawing.Icon((Join-Path $PSScriptRoot 'Spacewars.ico'))
$form.ClientSize = New-Object System.Drawing.Size(480,300)
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.StartPosition = 'CenterScreen'
$form.AutoScaleMode = 'Dpi'
$form.BackColor = [System.Drawing.Color]::FromArgb(14,23,36)
$form.ForeColor = [System.Drawing.Color]::WhiteSmoke
function Label($text,$size,$y) {
 $c = New-Object System.Windows.Forms.Label
 $c.Text=$text; $c.Location=New-Object System.Drawing.Point(32,$y)
 $c.Size=New-Object System.Drawing.Size(416,34)
 $c.Font=New-Object System.Drawing.Font('Segoe UI',$size)
 $form.Controls.Add($c); return $c
}
$title=Label 'SPACEWARS' 24 32
$version=Label '' 10 80
$version.ForeColor=[System.Drawing.Color]::LightSlateGray
$message=Label '' 11 125
$progress=New-Object System.Windows.Forms.ProgressBar
$progress.Location=New-Object System.Drawing.Point(32,178)
$progress.Size=New-Object System.Drawing.Size(416,6)
$progress.Visible=$false; $form.Controls.Add($progress)
function Button($text,$x,$width) {
 $b=New-Object System.Windows.Forms.Button
 $b.Text=$text; $b.Location=New-Object System.Drawing.Point($x,218)
 $b.Size=New-Object System.Drawing.Size($width,48)
 $b.Font=New-Object System.Drawing.Font('Segoe UI',12)
 $b.FlatStyle='Flat';$b.FlatAppearance.BorderSize=0
 $b.BackColor=[System.Drawing.Color]::FromArgb(36,54,74)
 $form.Controls.Add($b);return $b
}
$play=Button 'Играть' 32 416
$play.BackColor=[System.Drawing.Color]::FromArgb(77,215,203)
$play.ForeColor=[System.Drawing.Color]::FromArgb(9,35,39)
$secondary=Button '' 246 202
$secondary.Visible=$false
$form.AcceptButton=$play
$play.Add_Click({[Console]::Out.WriteLine('play');[Console]::Out.Flush()})
$secondary.Add_Click({[Console]::Out.WriteLine('secondary');[Console]::Out.Flush()})
Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Threading;
public static class LauncherInput {
 public static readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();
 public static volatile bool Ended;
 public static void Start() {
  var reader = new Thread(() => {
   try { string line; while ((line = Console.ReadLine()) != null) {
    if (line.Length > 16384) break;
    Lines.Enqueue(line);
   }} finally { Ended = true; }
  });
  reader.IsBackground = true; reader.Start();
 }
}
'@
[LauncherInput]::Start()
$timer=New-Object System.Windows.Forms.Timer
$timer.Interval=100
$timer.Add_Tick({
 $line=$null
 if(![LauncherInput]::Lines.TryDequeue([ref]$line)){
  if([LauncherInput]::Ended){$form.Close()}
  return
 }
 try {
  $s=$line|ConvertFrom-Json
  $version.Text=$s.version;$message.Text=$s.message
  $play.Text=$s.primary;$play.Enabled=!$s.busy
  $secondary.Text=$s.secondary;$secondary.Visible=![string]::IsNullOrEmpty($s.secondary)
  $secondary.Enabled=!$s.busy
  if($secondary.Visible){$play.Width=200}else{$play.Width=416}
  $progress.Visible=$s.message.StartsWith('Загрузка') -or $s.message -eq 'Установка…'
  if($s.progress -lt 0){$progress.Style='Marquee'}else{$progress.Style='Continuous';$progress.Value=[Math]::Max(0,[Math]::Min(100,$s.progress))}
 } catch { $form.Close();return }
})
$form.Add_Shown({$timer.Start()})
try {[System.Windows.Forms.Application]::Run($form)}finally{$timer.Stop();$timer.Dispose();$form.Dispose()}
