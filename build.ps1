param([string]$OutputName = 'PhotoString.exe')
$ErrorActionPreference = 'Stop'
if ([System.IO.Path]::GetFileName($OutputName) -ne $OutputName -or [System.IO.Path]::GetExtension($OutputName) -ne '.exe') { throw 'OutputName must be an executable filename.' }
$taskRoot = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkPath 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
$outputPath = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
& (Join-Path $taskRoot 'generate-icon.ps1')
$references = @('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output',('/win32manifest:' + (Join-Path $taskRoot 'app.manifest')),('/win32icon:' + (Join-Path $taskRoot 'assets\PhotoString.ico')),('/out:' + (Join-Path $outputPath $OutputName)))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $frameworkPath $reference) }
$arguments += Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output ('Built: ' + (Join-Path $outputPath $OutputName))
