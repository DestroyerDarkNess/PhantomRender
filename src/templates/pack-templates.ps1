param(
    [string]$PhantomRenderVersion = "0.1.0-preview.2",
    [string]$PhantomRenderImGuiVersion = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PSScriptRoot "artifacts"
}

if ([string]::IsNullOrWhiteSpace($PhantomRenderImGuiVersion)) {
    $PhantomRenderImGuiVersion = $PhantomRenderVersion
}

$templateTokenMap = @{
    "__PHANTOMRENDER_NUGET_VERSION__" = $PhantomRenderVersion
    "__PHANTOMRENDER_IMGUI_NUGET_VERSION__" = $PhantomRenderImGuiVersion
}
$templateSpecs = @(
    @{
        Source = Join-Path $PSScriptRoot "PhantomRender.NativeAot.Template"
        ZipName = "PhantomRender.NativeAot.Template.zip"
    },
    @{
        Source = Join-Path $PSScriptRoot "PhantomRender.NetFramework.Template"
        ZipName = "PhantomRender.NetFramework.Template.zip"
    }
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$textExtensions = @(
    ".bat",
    ".cmd",
    ".config",
    ".cs",
    ".csproj",
    ".json",
    ".md",
    ".props",
    ".ps1",
    ".targets",
    ".txt",
    ".vstemplate",
    ".xml"
)
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PhantomRenderTemplates-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

try {
    foreach ($spec in $templateSpecs) {
        if (-not (Test-Path $spec.Source)) {
            throw "Template source folder was not found: $($spec.Source)"
        }

        $stageDir = Join-Path $stagingRoot ([System.IO.Path]::GetFileName($spec.Source))
        New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

        foreach ($file in Get-ChildItem -Path $spec.Source -Recurse -File) {
            $relativePath = $file.FullName.Substring($spec.Source.Length).TrimStart('\', '/')
            $destinationPath = Join-Path $stageDir $relativePath
            $destinationDir = Split-Path -Parent $destinationPath

            if (-not [string]::IsNullOrWhiteSpace($destinationDir)) {
                New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
            }

            $extension = [System.IO.Path]::GetExtension($file.Name)
            if ($textExtensions -contains $extension.ToLowerInvariant()) {
                $content = [System.IO.File]::ReadAllText($file.FullName)
                foreach ($token in $templateTokenMap.Keys) {
                    $content = $content.Replace($token, $templateTokenMap[$token])
                }
                [System.IO.File]::WriteAllText($destinationPath, $content, $utf8NoBom)
            }
            else {
                [System.IO.File]::Copy($file.FullName, $destinationPath, $true)
            }
        }

        $zipPath = Join-Path $OutputDir $spec.ZipName
        if (Test-Path $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $stageDir,
            $zipPath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)

        Write-Host "Generated $zipPath"
    }
}
finally {
    if (Test-Path $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
