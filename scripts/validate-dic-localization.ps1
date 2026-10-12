param(
    [string]$Root = "."
)

$ErrorActionPreference = "Stop"

$validationErrors = New-Object System.Collections.Generic.List[string]

function Add-ValidationError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $validationErrors.Add($Message)
}

function Get-JsonProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return $Object.PSObject.Properties[$Name]
}

function Get-BusinessRootProperties {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Document
    )

    return @($Document.PSObject.Properties | Where-Object {
        $_.Name -ne "schemaVersion" -and $_.Name -ne "type"
    })
}

function Get-DictionaryFileStem {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fileStem = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    if ($fileStem.EndsWith(".dic", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fileStem.Substring(0, $fileStem.Length - 4)
    }

    return $fileStem
}

$resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
$languagesPath = Join-Path $resolvedRoot "frontend/modules/platform/metadata/languages.json"

if (-not (Test-Path -LiteralPath $languagesPath)) {
    Add-ValidationError ("Localization languages manifest not found: {0}" -f $languagesPath)
}

$supportedLanguages = @()
if (Test-Path -LiteralPath $languagesPath) {
    try {
        $languagesDocument = Get-Content -Raw -LiteralPath $languagesPath -Encoding UTF8 | ConvertFrom-Json
        $supportedLanguages = @($languagesDocument.supportedLanguages | ForEach-Object { $_.code } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    catch {
        Add-ValidationError ("Localization languages manifest '{0}' is not valid JSON: {1}" -f $languagesPath, $_.Exception.Message)
    }
}

if ($supportedLanguages.Count -eq 0) {
    Add-ValidationError ("Localization languages manifest '{0}' must declare at least one supported language." -f $languagesPath)
}

$modulesRoot = Join-Path $resolvedRoot "frontend/modules"
$dictionaryFiles = @()
if (Test-Path -LiteralPath $modulesRoot) {
    $dictionaryFiles = @(Get-ChildItem -LiteralPath $modulesRoot -Directory | ForEach-Object {
        $dictionaryRoot = Join-Path $_.FullName "metadata/dictionaries"
        if (Test-Path -LiteralPath $dictionaryRoot) {
            Get-ChildItem -LiteralPath $dictionaryRoot -Filter "*.dic.json" -File -Recurse
        }
    })
}

foreach ($dictionaryFile in $dictionaryFiles) {
    $dictionaryPath = $dictionaryFile.FullName

    try {
        $document = Get-Content -Raw -LiteralPath $dictionaryPath -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Add-ValidationError ("Dictionary '{0}' is not valid JSON: {1}" -f $dictionaryPath, $_.Exception.Message)
        continue
    }

    $typeProperty = Get-JsonProperty -Object $document -Name "type"
    if ($null -eq $typeProperty -or $typeProperty.Value -ne "dic") {
        Add-ValidationError ("Dictionary '{0}' must declare type 'dic'." -f $dictionaryPath)
        continue
    }

    $businessRoots = @(Get-BusinessRootProperties -Document $document)
    if ($businessRoots.Count -ne 1) {
        Add-ValidationError ("Dictionary '{0}' must contain exactly one business root object." -f $dictionaryPath)
        continue
    }

    $businessRoot = $businessRoots[0]
    $fileStem = Get-DictionaryFileStem -Path $dictionaryPath
    if ($fileStem -cne $businessRoot.Name) {
        Add-ValidationError ("Dictionary '{0}' file name '{1}' does not match business root '{2}'." -f $dictionaryPath, $fileStem, $businessRoot.Name)
    }

    if ($null -eq $businessRoot.Value -or $businessRoot.Value -isnot [pscustomobject]) {
        Add-ValidationError ("Dictionary '{0}' business root '{1}' must be an object." -f $dictionaryPath, $businessRoot.Name)
        continue
    }

    foreach ($entry in $businessRoot.Value.PSObject.Properties) {
        if ($entry.Name.Contains(".")) {
            Add-ValidationError ("Dictionary '{0}' entry key '{1}' must not contain '.'." -f $dictionaryPath, $entry.Name)
        }

        if ($null -eq $entry.Value -or $entry.Value -isnot [pscustomobject]) {
            Add-ValidationError ("Dictionary '{0}' entry '{1}' must be an object." -f $dictionaryPath, $entry.Name)
            continue
        }

        foreach ($supportedLanguage in $supportedLanguages) {
            $translationProperty = Get-JsonProperty -Object $entry.Value -Name $supportedLanguage
            if ($null -eq $translationProperty) {
                Add-ValidationError ("Dictionary '{0}' entry '{1}' is missing supported language '{2}'." -f $dictionaryPath, $entry.Name, $supportedLanguage)
                continue
            }

            if ($translationProperty.Value -isnot [string]) {
                Add-ValidationError ("Dictionary '{0}' entry '{1}' language '{2}' must be a string." -f $dictionaryPath, $entry.Name, $supportedLanguage)
            }
        }

        foreach ($translation in $entry.Value.PSObject.Properties) {
            if ($supportedLanguages -notcontains $translation.Name) {
                Add-ValidationError ("Dictionary '{0}' entry '{1}' contains unsupported language '{2}'." -f $dictionaryPath, $entry.Name, $translation.Name)
            }
        }
    }
}

if ($validationErrors.Count -gt 0) {
    foreach ($validationError in $validationErrors) {
        Write-Error $validationError
    }

    exit 1
}

Write-Host ("dic localization validation passed for {0} file(s)." -f $dictionaryFiles.Count) -ForegroundColor Green

# 最小认证词典与正式 authoring 同源，静态检查拒绝未重新生成的产物。
& node (Join-Path $resolvedRoot "frontend/scripts/generate-bootstrap-localization.mjs") --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
