param(
    [Parameter(Mandatory = $true)]
    [string[]]$InputPath,

    [string]$OutputRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "resources\localization"),

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

        $enabledLanguages[$language.code] = $true
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
        [string]$ContextName
    )

    if (-not $Catalogs.ContainsKey($LanguageCode)) {
        $Catalogs[$LanguageCode] = @{}
    }

    $languageCatalog = $Catalogs[$LanguageCode]
    if (-not $languageCatalog.ContainsKey($ContextName)) {
        $languageCatalog[$ContextName] = @{}
    }

    return $languageCatalog[$ContextName]
}

$languageConfig = Read-LanguagesConfig -ConfigPath $LanguagesConfigPath
$catalogs = @{}
$warnings = New-Object System.Collections.Generic.List[string]
$writtenFiles = New-Object System.Collections.Generic.List[string]

foreach ($rawInputPath in $InputPath) {
    $resolvedInputPath = Resolve-RequiredPath -Path $rawInputPath -Description "Dictionary input"
    $dictionaryXml = [xml](Get-Content -Raw -LiteralPath $resolvedInputPath -Encoding UTF8)

    foreach ($context in $dictionaryXml.DictionarySerializer.Context) {
        $contextName = [string]$context.Name
        foreach ($label in $context.Label) {
            $labelName = [string]$label.Name
            $resourceKey = "$contextName.$labelName"

            foreach ($localizedLabel in $label.LocalizedLabel) {
                $languageCode = [string]$localizedLabel.LanguageRef
                if (-not $languageConfig.EnabledLanguages.ContainsKey($languageCode)) {
                    $warnings.Add("Skipped unsupported language '$languageCode' for key '$resourceKey' in '$resolvedInputPath'.") | Out-Null
                    continue
                }

                $contextCatalog = Get-OrCreateLanguageCatalog -Catalogs $catalogs -LanguageCode $languageCode -ContextName $contextName
                $value = [string]$localizedLabel.InnerText

                if ($contextCatalog.ContainsKey($resourceKey) -and $contextCatalog[$resourceKey] -ne $value) {
                    $warnings.Add("Overrode key '$resourceKey' for language '$languageCode' using later input '$resolvedInputPath'.") | Out-Null
                }

                $contextCatalog[$resourceKey] = $value
            }
        }
    }
}

foreach ($languageCode in $catalogs.Keys) {
    $languageRoot = Join-Path $OutputRoot $languageCode
    $dictionaryRoot = Join-Path $languageRoot "dictionary"
    New-Item -ItemType Directory -Force -Path $dictionaryRoot | Out-Null

    foreach ($contextName in $catalogs[$languageCode].Keys) {
        $outputPath = Join-Path $dictionaryRoot "$contextName.json"
        $json = $catalogs[$languageCode][$contextName] | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText($outputPath, "$json`n", [System.Text.UTF8Encoding]::new($false))
        $writtenFiles.Add($outputPath) | Out-Null
    }
}

if ($warnings.Count -gt 0) {
    foreach ($warning in $warnings) {
        Write-Warning $warning
    }
}

Write-Host "Dictionary i18n conversion completed." -ForegroundColor Green
Write-Host "Output root: $OutputRoot"
Write-Host "Written files: $($writtenFiles.Count)"
