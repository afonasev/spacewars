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
$form.ClientSize = New-Object System.Drawing.Size(620,350)
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.StartPosition = 'CenterScreen'
$form.AutoScaleMode = 'Dpi'
$form.BackColor = [System.Drawing.Color]::FromArgb(14,23,36)
$form.ForeColor = [System.Drawing.Color]::WhiteSmoke
function Label($text,$size,$y) {
 $c = New-Object System.Windows.Forms.Label
 $c.Text=$text; $c.Location=New-Object System.Drawing.Point(26,$y)
 $c.Size=New-Object System.Drawing.Size(568,40)
 $c.Font=New-Object System.Drawing.Font('Segoe UI',$size)
 $form.Controls.Add($c); return $c
}
$logoSource=[System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'LauncherLogo.png'))
# The fixed cover crops the existing game logo; it never stretches the planet.
$coverImage=New-Object System.Drawing.Bitmap(568,64)
$canvas=[System.Drawing.Graphics]::FromImage($coverImage)
$scale=[Math]::Max(568.0/$logoSource.Width,64.0/$logoSource.Height)
$canvas.DrawImage($logoSource,[single]((568-$logoSource.Width*$scale)/2),[single]((64-$logoSource.Height*$scale)/2),[single]($logoSource.Width*$scale),[single]($logoSource.Height*$scale))
$canvas.Dispose();$logoSource.Dispose()
$cover=New-Object System.Windows.Forms.PictureBox
$cover.Location=New-Object System.Drawing.Point(26,22);$cover.Size=New-Object System.Drawing.Size(568,64)
$cover.Image=$coverImage;$form.Controls.Add($cover)
$title=Label 'SPACEWARS' 18 0
$form.Controls.Remove($title);$cover.Controls.Add($title)
$title.Location=New-Object System.Drawing.Point(0,0);$title.Size=New-Object System.Drawing.Size(280,64)
$title.Padding=New-Object System.Windows.Forms.Padding(16,14,0,0)
$title.BackColor=[System.Drawing.Color]::FromArgb(14,23,36)
$heading=Label '' 20 108
$heading.Font=New-Object System.Drawing.Font('Segoe UI',20,[System.Drawing.FontStyle]::Bold)
$version=Label '' 9.5 231
$version.Height=30
$version.ForeColor=[System.Drawing.Color]::LightSlateGray
$message=Label '' 11 153
$message.Height=62
$progress=New-Object System.Windows.Forms.ProgressBar
$progress.Location=New-Object System.Drawing.Point(26,271)
$progress.Size=New-Object System.Drawing.Size(568,6)
$progress.Visible=$false; $form.Controls.Add($progress)
function Button($text,$x,$width) {
 $b=New-Object System.Windows.Forms.Button
 $b.Text=$text; $b.Location=New-Object System.Drawing.Point($x,286)
 $b.Size=New-Object System.Drawing.Size($width,44)
 $b.Font=New-Object System.Drawing.Font('Segoe UI',10)
 $b.FlatStyle='Flat';$b.FlatAppearance.BorderSize=0
 $b.BackColor=[System.Drawing.Color]::FromArgb(36,54,74)
 $form.Controls.Add($b);return $b
}
$play=Button 'Играть сейчас' 420 174
$play.BackColor=[System.Drawing.Color]::FromArgb(77,215,203)
$play.ForeColor=[System.Drawing.Color]::FromArgb(9,35,39)
$secondary=Button '' 436 158
$secondary.Visible=$false
$form.AcceptButton=$play
$play.Add_Click({[Console]::Out.WriteLine('play');[Console]::Out.Flush()})
$secondary.Add_Click({
 if($secondary.Text.StartsWith('Обновить') -or $secondary.Text.StartsWith('Установить') -or $secondary.Text -eq 'Повторить'){
  $answer=[System.Windows.Forms.MessageBox]::Show($form,'Согласны установить обновление Spacewars?','Обновление Spacewars',[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Question,[System.Windows.Forms.MessageBoxDefaultButton]::Button2)
  if($answer -ne [System.Windows.Forms.DialogResult]::Yes){return}
 }
 [Console]::Out.WriteLine('secondary');[Console]::Out.Flush()
})
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
  $heading.Text=$s.title;$version.Text=$s.version;$message.Text=$s.message
  $play.Text=$s.primary;$play.Enabled=!$s.busy
  $secondary.Text=$s.secondary;$secondary.Visible=![string]::IsNullOrEmpty($s.secondary)
  $secondary.Enabled=!$s.busy
  if($secondary.Visible){$play.Left=250}else{$play.Left=420}
  $updateAccent=$secondary.Visible -and $s.secondary -ne 'Отменить'
  $accent=[System.Drawing.Color]::FromArgb(77,215,203);$neutral=[System.Drawing.Color]::FromArgb(36,54,74);$ink=[System.Drawing.Color]::FromArgb(9,35,39)
  if($updateAccent){$play.BackColor=$neutral;$play.ForeColor=[System.Drawing.Color]::WhiteSmoke;$secondary.BackColor=$accent;$secondary.ForeColor=$ink}
  else{$play.BackColor=$accent;$play.ForeColor=$ink;$secondary.BackColor=$neutral;$secondary.ForeColor=[System.Drawing.Color]::WhiteSmoke}
  $progress.Visible=$s.message.StartsWith('Загрузка') -or $s.message -eq 'Установка…'
  if($s.progress -lt 0){$progress.Style='Marquee'}else{$progress.Style='Continuous';$progress.Value=[Math]::Max(0,[Math]::Min(100,$s.progress))}
 } catch { $form.Close();return }
})
$form.Add_Shown({$timer.Start()})
try {[System.Windows.Forms.Application]::Run($form)}finally{$timer.Stop();$timer.Dispose();$form.Dispose();$coverImage.Dispose()}
