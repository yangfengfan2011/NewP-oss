param(
    [Parameter(Mandatory = $true)]
    [string]$EnumerationPath,

    [Parameter(Mandatory = $true)]
    [string]$DictionaryPath,

    [string]$OutputRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "resources"),

    [string]$LanguagesConfigPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "resources\localization\languages.json")
)

$ErrorActionPreference = "Stop"

function Resolve-RequiredPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    try {
        return (Resolve-Path -LiteralPath $Path).Path
    }
    catch {
        throw "$Description not found: $Path"
    }
}

function Read-LanguagesConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigPath
    )

    $resolvedConfigPath = Resolve-RequiredPath -Path $ConfigPath -Description "Languages config"
    $config = Get-Content -Raw -LiteralPath $resolvedConfigPath -Encoding UTF8 | ConvertFrom-Json

    if (-not $config.defaultLanguage) {
        throw "Languages config must provide defaultLanguage."
    }

    if (-not $config.supportedLanguages -or $config.supportedLanguages.Count -eq 0) {
        throw "Languages config must provide supportedLanguages."
    }

    $enabledLanguages = @{}
    foreach ($language in $config.supportedLanguages) {
        if (-not $language.code) {
            throw "Each supported language must provide code."
        }

        $enabledLanguages[[string]$language.code] = $true
    }

    return @{
        DefaultLanguage = [string]$config.defaultLanguage
        EnabledLanguages = $enabledLanguages
    }
}

function Get-OrCreateLanguageCatalog {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Catalogs,

        [Parameter(Mandatory = $true)]
        [string]$LanguageCode,

        [Parameter(Mandatory = $true)]
        [string]$EnumerationName
    )

    if (-not $Catalogs.ContainsKey($LanguageCode)) {
        $Catalogs[$LanguageCode] = @{}
    }

    $languageCatalog = $Catalogs[$LanguageCode]
    if (-not $languageCatalog.ContainsKey($EnumerationName)) {
        $languageCatalog[$EnumerationName] = @{}
    }

    return $languageCatalog[$EnumerationName]
}

$languageConfig = Read-LanguagesConfig -ConfigPath $LanguagesConfigPath
$resolvedEnumerationPath = Resolve-RequiredPath -Path $EnumerationPath -Description "Enumeration input"
$resolvedDictionaryPath = Resolve-RequiredPath -Path $DictionaryPath -Description "Enumeration dictionary input"

$enumerationXml = [xml](Get-Content -Raw -LiteralPath $resolvedEnumerationPath -Encoding UTF8)
$dictionaryXml = [xml](Get-Content -Raw -LiteralPath $resolvedDictionaryPath -Encoding UTF8)

$warnings = New-Object System.Collections.Generic.List[string]
$structureFiles = New-Object System.Collections.Generic.List[string]
$localizationFiles = New-Object System.Collections.Generic.List[string]
$catalogs = @{}
$labelLookup = @{}
$consumedLabels = @{}

foreach ($context in @($dictionaryXml.DictionarySerializer.Context)) {
    foreach ($label in @($context.Label)) {
        if ($null -eq $label) {
            continue
        }

        $labelName = [string]$label.Name
        if (-not $labelName) {
            continue
        }

        if (-not $labelLookup.ContainsKey($labelName)) {
            $labelLookup[$labelName] = New-Object System.Collections.Generic.List[object]
        }

        $labelLookup[$labelName].Add($label) | Out-Null
    }
}

$enumerationsRoot = Join-Path $OutputRoot "enumerations"
$localizationRoot = Join-Path $OutputRoot "localization"
New-Item -ItemType Directory -Force -Path $enumerationsRoot | Out-Null

foreach ($enumeration in @($enumerationXml.root.Enumeration)) {
    if ($null -eq $enumeration) {
        continue
    }

    $enumerationName = [string]$enumeration.Name
    if (-not $enumerationName) {
        continue
    }

    $structure = [ordered]@{
        name = $enumerationName
        items = @()
    }

    foreach ($item in @($enumeration.EnumerationItem)) {
        if ($null -eq $item) {
            continue
        }

        $itemName = [string]$item.Name
        if (-not $itemName) {
            continue
        }

        $structure.items += [ordered]@{
            name = $itemName
        }

        $labelName = "$enumerationName$itemName"
        if (-not $labelLookup.ContainsKey($labelName)) {
            $warnings.Add("Missing localization label for '$enumerationName.$itemName' in '$resolvedDictionaryPath'; consumers should fall back to '$itemName'.") | Out-Null
            continue
        }

        $matchingLabels = $labelLookup[$labelName]
        if ($matchingLabels.Count -gt 1) {
            $warnings.Add("Found multiple localization labels for '$labelName' in '$resolvedDictionaryPath'; using the first match.") | Out-Null
        }

        $consumedLabels[$labelName] = $true
        $selectedLabel = $matchingLabels[0]
        $localizedByLanguage = @{}

        foreach ($localizedLabel in @($selectedLabel.LocalizedLabel)) {
            if ($null -eq $localizedLabel) {
                continue
            }

            $languageCode = [string]$localizedLabel.LanguageRef
            if (-not $languageCode) {
                continue
            }

            if (-not $languageConfig.EnabledLanguages.ContainsKey($languageCode)) {
                $warnings.Add("Skipped unsupported language '$languageCode' for '$enumerationName.$itemName' in '$resolvedDictionaryPath'.") | Out-Null
                continue
            }

            $value = [string]$localizedLabel.InnerText
            if ($localizedByLanguage.ContainsKey($languageCode) -and $localizedByLanguage[$languageCode] -ne $value) {
                $warnings.Add("Overrode duplicate language '$languageCode' for '$enumerationName.$itemName' in '$resolvedDictionaryPath'.") | Out-Null
            }

            $localizedByLanguage[$languageCode] = $value
        }

        foreach ($enabledLanguageCode in $languageConfig.EnabledLanguages.Keys) {
            if (-not $localizedByLanguage.ContainsKey($enabledLanguageCode)) {
                $warnings.Add("Missing enabled language '$enabledLanguageCode' for '$enumerationName.$itemName' in '$resolvedDictionaryPath'.") | Out-Null
                continue
            }

            $catalog = Get-OrCreateLanguageCatalog -Catalogs $catalogs -LanguageCode $enabledLanguageCode -EnumerationName $enumerationName
            $catalog["$enumerationName.$itemName"] = $localizedByLanguage[$enabledLanguageCode]
        }
    }

    $structurePath = Join-Path $enumerationsRoot "$enumerationName.json"
    $structureJson = $structure | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($structurePath, "$structureJson`n", [System.Text.UTF8Encoding]::new($false))
    $structureFiles.Add($structurePath) | Out-Null
}

foreach ($labelName in $labelLookup.Keys) {
    if (-not $consumedLabels.ContainsKey($labelName)) {
        $warnings.Add("Skipped orphan localization label '$labelName' because no matching enumeration item exists in '$resolvedEnumerationPath'.") | Out-Null
    }
}

foreach ($languageCode in $catalogs.Keys) {
    $languageEnumerationRoot = Join-Path (Join-Path $localizationRoot $languageCode) "enumerations"
    New-Item -ItemType Directory -Force -Path $languageEnumerationRoot | Out-Null

    foreach ($enumerationName in $catalogs[$languageCode].Keys) {
        $localizationPath = Join-Path $languageEnumerationRoot "$enumerationName.json"
        $localizationJson = $catalogs[$languageCode][$enumerationName] | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText($localizationPath, "$localizationJson`n", [System.Text.UTF8Encoding]::new($false))
        $localizationFiles.Add($localizationPath) | Out-Null
    }
}

if ($warnings.Count -gt 0) {
    foreach ($warning in $warnings) {
        Write-Warning $warning
    }
}

Write-Host "Enumeration static resource conversion completed." -ForegroundColor Green
Write-Host "Output root: $OutputRoot"
Write-Host "Structure files: $($structureFiles.Count)"
Write-Host "Localization files: $($localizationFiles.Count)"
