param(
    [string]$PhantomRenderVersion = "0.1.0-preview.1",
    [string]$VisualStudioYear = "2022"
)

$ErrorActionPreference = "Stop"

$artifactDir = Join-Path $PSScriptRoot "artifacts"
$targetDir = Join-Path ([Environment]::GetFolderPath("MyDocuments")) ("Visual Studio " + $VisualStudioYear + "\Templates\ProjectTemplates\PhantomRender")

& (Join-Path $PSScriptRoot "pack-templates.ps1") -PhantomRenderVersion $PhantomRenderVersion -OutputDir $artifactDir

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Copy-Item -Path (Join-Path $artifactDir "*.zip") -Destination $targetDir -Force

Write-Host "Installed PhantomRender templates into:"
Write-Host "  $targetDir"
Write-Host
Write-Host "Restart Visual Studio. If the templates do not appear immediately, run:"
Write-Host "  devenv /installvstemplates"
