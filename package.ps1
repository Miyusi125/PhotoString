$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$shareRoot = Join-Path $projectRoot 'share'
$sourceRoot = Join-Path $shareRoot 'github'
$runtimeRoot = Join-Path $shareRoot 'windows-x64'
$checkRoot = Join-Path $projectRoot '_local\package-check'
$buildName = 'PhotoString.package-' + [Guid]::NewGuid().ToString('N') + '.exe'
$buildPath = Join-Path $projectRoot ('dist\' + $buildName)
$sourceFiles = @(
    '.gitignore', 'README.md', 'app.manifest', 'PhotoString.csproj',
    'build.ps1', 'generate-icon.ps1', 'test.ps1', 'package.ps1',
    'assets\PhotoString.ico', 'docs\USER_GUIDE.md', 'docs\catalog-preview.png'
)
$sourceFiles += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File |
    Sort-Object Name | ForEach-Object { 'src\' + $_.Name }
$runtimeFiles = @('PhotoString.exe', 'README.md')

function Assert-WorkspacePath([string]$path) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($projectRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw ('Output path is outside the project: ' + $path)
    }
    $cursor = $fullPath
    while ($cursor.Length -gt $projectRoot.Length) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw ('Output path contains a link or junction: ' + $cursor)
            }
        }
        $cursor = [System.IO.Path]::GetDirectoryName($cursor)
    }
}

function Assert-PackageContents([string]$folder, [string[]]$allowedFiles) {
    Assert-WorkspacePath $folder
    if (-not (Test-Path -LiteralPath $folder)) { return }
    foreach ($item in Get-ChildItem -LiteralPath $folder -Recurse -Force) {
        $relativePath = $item.FullName.Substring($folder.Length + 1)
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            throw ('Package output contains a link or junction: ' + $item.FullName)
        }
        if ($item.PSIsContainer) {
            $prefix = $relativePath + '\'
            $matches = @($allowedFiles | Where-Object { $_.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) })
            if ($matches.Count -eq 0) { throw ('Move local files out of the package output first: ' + $item.FullName) }
        } elseif ($allowedFiles -notcontains $relativePath) {
            throw ('Move local files out of the package output first: ' + $item.FullName)
        }
    }
}

function Write-PackageZip([string]$folder, [string[]]$files, [string]$destination) {
    Assert-WorkspacePath $destination
    $temporaryZip = $destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $stream = [System.IO.File]::Open($temporaryZip, [System.IO.FileMode]::CreateNew)
    $archive = $null
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
        foreach ($relativePath in $files) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, (Join-Path $folder $relativePath), $relativePath.Replace('\', '/'),
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    } finally {
        if ($archive) { $archive.Dispose() }
        $stream.Dispose()
    }
    Move-Item -LiteralPath $temporaryZip -Destination $destination -Force
}

Assert-PackageContents $sourceRoot $sourceFiles
Assert-PackageContents $runtimeRoot $runtimeFiles
Assert-WorkspacePath $buildPath
Assert-WorkspacePath $checkRoot
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

try {
    & (Join-Path $projectRoot 'build.ps1') -OutputName $buildName
    New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
    $testProcess = Start-Process -FilePath $buildPath -ArgumentList @('--self-test', ('"' + $checkRoot + '"')) -WindowStyle Hidden -PassThru
    if (-not $testProcess.WaitForExit(120000)) {
        $testProcess.Kill()
        $testProcess.WaitForExit()
        throw 'Self-test timed out. No packages were created.'
    }
    if ($testProcess.ExitCode -ne 0) { throw ('Self-test failed. See ' + (Join-Path $checkRoot 'verification.txt')) }
    $checks = @(Get-Content -LiteralPath (Join-Path $checkRoot 'verification.txt') -Encoding UTF8 | Where-Object { $_.StartsWith('PASS ') })
    if ($checks.Count -eq 0) { throw 'The self-test did not report any passing checks.' }

    New-Item -ItemType Directory -Path $sourceRoot, $runtimeRoot -Force | Out-Null
    foreach ($relativePath in $sourceFiles) {
        $destination = Join-Path $sourceRoot $relativePath
        New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $projectRoot $relativePath) -Destination $destination -Force
    }
    Copy-Item -LiteralPath $buildPath -Destination (Join-Path $runtimeRoot 'PhotoString.exe') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\USER_GUIDE.md') -Destination (Join-Path $runtimeRoot 'README.md') -Force
    Assert-PackageContents $sourceRoot $sourceFiles
    Assert-PackageContents $runtimeRoot $runtimeFiles

    $sourceZip = Join-Path $shareRoot 'PhotoString-Source.zip'
    $runtimeZip = Join-Path $shareRoot 'PhotoString-Windows.zip'
    Write-PackageZip $sourceRoot $sourceFiles $sourceZip
    Write-PackageZip $runtimeRoot $runtimeFiles $runtimeZip
    $hashes = foreach ($relativePath in @('PhotoString-Source.zip', 'PhotoString-Windows.zip', 'windows-x64\PhotoString.exe')) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $shareRoot $relativePath) -Algorithm SHA256).Hash.ToLowerInvariant()
        $hash + '  ' + $relativePath.Replace('\', '/')
    }
    [System.IO.File]::WriteAllLines((Join-Path $shareRoot 'SHA256SUMS.txt'), [string[]]$hashes, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ('Passed: ' + $checks.Count + ' self-test checks')
    Write-Output ('GitHub source: ' + $sourceRoot)
    Write-Output ('Windows package: ' + $runtimeZip)
} finally {
    if (Test-Path -LiteralPath $buildPath) {
        Assert-WorkspacePath $buildPath
        Remove-Item -LiteralPath $buildPath -Force
    }
}
