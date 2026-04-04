param(
    [string]$PhantomRenderVersion = "0.1.0-preview.2",
    [string]$PhantomRenderImGuiVersion = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PSScriptRoot "PhantomRender.Templates.Vsix\ProjectTemplates"
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
        FolderName = "PhantomRender.NativeAot.Template"
        TemplateFileName = "PhantomRender.NativeAot.vstemplate"
    },
    @{
        Source = Join-Path $PSScriptRoot "PhantomRender.NetFramework.Template"
        FolderName = "PhantomRender.NetFramework.Template"
        TemplateFileName = "PhantomRender.NetFramework.vstemplate"
    }
)

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

if (Test-Path $OutputDir) {
    Get-ChildItem -LiteralPath $OutputDir -Force | Remove-Item -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$manifestDoc = New-Object System.Xml.XmlDocument
$manifestDeclaration = $manifestDoc.CreateXmlDeclaration("1.0", "utf-8", $null)
$null = $manifestDoc.AppendChild($manifestDeclaration)
$manifestRoot = $manifestDoc.CreateElement("VSTemplateManifest", "http://schemas.microsoft.com/developer/vstemplatemanifest/2015")
$manifestRoot.SetAttribute("Version", "1.0")
$null = $manifestDoc.AppendChild($manifestRoot)

foreach ($spec in $templateSpecs) {
    if (-not (Test-Path $spec.Source)) {
        throw "Template source folder was not found: $($spec.Source)"
    }

    $templateOutputDir = Join-Path $OutputDir $spec.FolderName
    New-Item -ItemType Directory -Path $templateOutputDir -Force | Out-Null

    foreach ($file in Get-ChildItem -Path $spec.Source -Recurse -File) {
        $relativePath = $file.FullName.Substring($spec.Source.Length).TrimStart('\', '/')
        $destinationPath = Join-Path $templateOutputDir $relativePath
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

    $templatePath = Join-Path $templateOutputDir $spec.TemplateFileName
    [xml]$templateXml = Get-Content $templatePath
    $nsManager = New-Object System.Xml.XmlNamespaceManager($templateXml.NameTable)
    $nsManager.AddNamespace("vst", "http://schemas.microsoft.com/developer/vstemplate/2005")
    $templateData = $templateXml.SelectSingleNode("/vst:VSTemplate/vst:TemplateData", $nsManager)

    if ($null -eq $templateData) {
        throw "TemplateData node was not found in $templatePath"
    }

    $templateType = $templateXml.DocumentElement.GetAttribute("Type")
    if ([string]::IsNullOrWhiteSpace($templateType)) {
        $templateType = "Project"
    }

    $container = $manifestDoc.CreateElement("VSTemplateContainer", $manifestRoot.NamespaceURI)
    $container.SetAttribute("TemplateType", $templateType)

    $relativePathOnDisk = $manifestDoc.CreateElement("RelativePathOnDisk", $manifestRoot.NamespaceURI)
    $relativePathOnDisk.InnerText = $spec.FolderName
    $null = $container.AppendChild($relativePathOnDisk)

    $templateFileName = $manifestDoc.CreateElement("TemplateFileName", $manifestRoot.NamespaceURI)
    $templateFileName.InnerText = $spec.TemplateFileName
    $null = $container.AppendChild($templateFileName)

    $templateHeader = $manifestDoc.CreateElement("VSTemplateHeader", $manifestRoot.NamespaceURI)
    $null = $templateHeader.AppendChild($manifestDoc.ImportNode($templateData, $true))
    $null = $container.AppendChild($templateHeader)

    $null = $manifestRoot.AppendChild($container)
}

$manifestPath = Join-Path $OutputDir "templateManifest0.noloc.vstman"
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Encoding = $utf8NoBom
$settings.Indent = $true
$writer = [System.Xml.XmlWriter]::Create($manifestPath, $settings)

try {
    $manifestDoc.Save($writer)
}
finally {
    $writer.Dispose()
}

Write-Host "Prepared VSIX project templates in $OutputDir"
