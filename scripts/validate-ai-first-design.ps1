param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path
)

$ErrorActionPreference = "Stop"

function New-UnicodeString {
    param(
        [Parameter(Mandatory = $true)]
        [int[]]$CodePoints
    )

    return (-join ($CodePoints | ForEach-Object { [char]$_ }))
}

$requiredChecklistItems = @(
    (New-UnicodeString @(0x5165, 0x53E3, 0x5728, 0x54EA, 0x91CC)),
    (New-UnicodeString @(0x8FB9, 0x754C, 0x5728, 0x54EA, 0x91CC)),
    (New-UnicodeString @(0x5982, 0x4F55, 0x9A8C, 0x8BC1)),
    (New-UnicodeString @(0x5931, 0x8D25, 0x4FE1, 0x53F7, 0x662F, 0x4EC0, 0x4E48)),
    (New-UnicodeString @(0x5982, 0x4F55, 0x56DE, 0x6EDA))
)

$placeholderTerms = @(
    "TODO",
    "TBD",
    (New-UnicodeString @(0x5F85, 0x8865)),
    (New-UnicodeString @(0x5360, 0x4F4D))
)

$validationErrors = New-Object System.Collections.Generic.List[string]
$validatedFiles = New-Object System.Collections.Generic.List[string]

foreach ($rawPath in $Path) {
    $resolvedPath = $null
    try {
        $resolvedPath = (Resolve-Path -LiteralPath $rawPath).Path
    }
    catch {
        $validationErrors.Add(("File not found: {0}" -f $rawPath))
        continue
    }

    $content = Get-Content -Raw -LiteralPath $resolvedPath -Encoding UTF8
    $validatedFiles.Add($resolvedPath)

    foreach ($requiredItem in $requiredChecklistItems) {
        if ($content -notmatch [regex]::Escape($requiredItem)) {
            $validationErrors.Add(("[{0}] Missing AI-first checklist item: {1}" -f $resolvedPath, $requiredItem))
        }
    }

    foreach ($placeholder in $placeholderTerms) {
        if ($content -match [regex]::Escape($placeholder)) {
            $validationErrors.Add(("[{0}] Placeholder term found: {1}" -f $resolvedPath, $placeholder))
        }
    }
}

if ($validationErrors.Count -gt 0) {
    foreach ($errorMessage in $validationErrors) {
        Write-Error $errorMessage
    }

    exit 1
}

Write-Host ("AI-first design validation passed for {0} file(s)." -f $validatedFiles.Count) -ForegroundColor Green
foreach ($validatedFile in $validatedFiles) {
    Write-Host " - $validatedFile" -ForegroundColor DarkGreen
}
