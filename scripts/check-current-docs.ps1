[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path -LiteralPath $Root).Path
$repoPrefix = $repoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$pathComparison = if ($IsWindows) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
$retiredPrefix = 'docs/_retired/'
$errors = [System.Collections.Generic.List[string]]::new()

$documentPaths = @(
    git -c core.quotepath=false -C $repoRoot ls-files --cached --others --exclude-standard -- '*.md'
)
if ($LASTEXITCODE -ne 0) {
    throw '无法枚举当前 Markdown 文档。'
}

$currentDocuments = $documentPaths |
    ForEach-Object { $_.Replace('\', '/') } |
    Where-Object { $_ -and -not $_.StartsWith($retiredPrefix, $pathComparison) } |
    Sort-Object -Unique

$retiredReferencePattern = '(?i)(docs/(superpowers|designs|phases|prompts|bootstrap|migration|modules)/|docs/current-status\.md|check-ai-first-design-targets|new-design-doc)'
$machinePathPattern = '(?i)(?<![A-Za-z0-9])[A-Z]:[\\/](Users|Work|GitHub|Github|Projects|Source|Repos)[\\/]|/[a-z]/GitHub/'
$mojibakePattern = '�|瀹¤|閰嶇|寰呭疄|銆\?|锟'
$linkPattern = '!?\[[^\]]*\]\((?<target><[^>]+>|[^)\s]+)\)'

foreach ($relativePath in $currentDocuments) {
    $fullPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $errors.Add("MISSING_DOCUMENT $relativePath：文档不存在。")
        continue
    }

    $lines = @(Get-Content -LiteralPath $fullPath -Encoding utf8)
    $insideFence = $false

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        $lineNumber = $index + 1

        if ($line -match '^\s*(```|~~~)') {
            $insideFence = -not $insideFence
            continue
        }

        if ($line -match $retiredReferencePattern) {
            $errors.Add("RETIRED_WORKFLOW_REFERENCE $relativePath`:$lineNumber：引用了退役工作流或目录：$($Matches[0])")
        }

        if ($line -match $machinePathPattern) {
            $errors.Add("MACHINE_PATH $relativePath`:$lineNumber：包含本机绝对路径：$($Matches[0])")
        }

        if ($line -match $mojibakePattern) {
            $errors.Add("MOJIBAKE $relativePath`:$lineNumber：疑似乱码：$($Matches[0])")
        }

        if ($insideFence) {
            continue
        }

        foreach ($match in [regex]::Matches($line, $linkPattern)) {
            $target = $match.Groups['target'].Value.Trim('<', '>')
            if ([string]::IsNullOrWhiteSpace($target) -or
                $target.StartsWith('#') -or
                $target.StartsWith('/') -or
                $target.StartsWith('//') -or
                $target -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
                continue
            }

            $targetWithoutFragment = ($target -split '[#?]', 2)[0]
            if ([string]::IsNullOrWhiteSpace($targetWithoutFragment)) {
                continue
            }

            $decodedTarget = [System.Uri]::UnescapeDataString($targetWithoutFragment).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $resolvedTarget = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $fullPath) $decodedTarget))

            if (-not $resolvedTarget.Equals($repoRoot, $pathComparison) -and
                -not $resolvedTarget.StartsWith($repoPrefix, $pathComparison)) {
                $errors.Add("LINK_ESCAPES_REPOSITORY $relativePath`:$lineNumber：链接越出仓库：$target")
                continue
            }

            $resolvedRelativeTarget = [System.IO.Path]::GetRelativePath($repoRoot, $resolvedTarget).Replace('\', '/')

            if ($resolvedRelativeTarget.StartsWith($retiredPrefix, $pathComparison)) {
                $errors.Add("LINK_TO_RETIRED $relativePath`:$lineNumber：当前文档链接到退役目录：$target")
                continue
            }

            if (-not (Test-Path -LiteralPath $resolvedTarget)) {
                $errors.Add("BROKEN_LINK $relativePath`:$lineNumber：失效链接：$target")
            }
        }
    }
}

if ($errors.Count -gt 0) {
    [Console]::Error.WriteLine("CURRENT_DOCS_CHECK_FAILED: $($errors.Count) 个问题")
    foreach ($errorMessage in $errors) {
        [Console]::Error.WriteLine(" - $errorMessage")
    }
    exit 1
}

Write-Host "CURRENT_DOCS_CHECK_PASSED: $($currentDocuments.Count) documents" -ForegroundColor Green
