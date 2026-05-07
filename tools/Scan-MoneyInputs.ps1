param(
    [string]$Root = ".\Assetra.WPF\Features",
    [switch]$FailOnFinding
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Root)) {
    throw "Root path not found: $Root"
}

$moneyNamePattern = 'Amount|Price|Cost|Balance|Value|Premium|Salary|Saving|Savings|Cash|Fee|Deposit|Mortgage'
$nonMoneyNamePattern = 'Note|Hint|Label|Name|Description|Return|Rate|Percent|Age|Years|Months|Count|Quantity|Shares|Stock|Symbol'

$findings = foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -Include *.xaml -File) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($text, '<TextBox\b[\s\S]*?(?:/>|</TextBox>)')) {
        $block = $match.Value
        $binding = [regex]::Match($block, 'Binding\s+([^,}\s]+)')
        if (-not $binding.Success) {
            continue
        }

        $bindingPath = $binding.Groups[1].Value
        if ($bindingPath -notmatch $moneyNamePattern -or $bindingPath -match $nonMoneyNamePattern) {
            continue
        }

        $hasMoneyStyle = $block -match 'AppMoneyTextBox'
        $hasSeparatorBehavior = $block -match 'ThousandSeparatorBehavior\.IsEnabled="True"'
        if ($hasMoneyStyle -or $hasSeparatorBehavior) {
            continue
        }

        $line = ($text.Substring(0, $match.Index) -split "`r?`n").Count
        [pscustomobject]@{
            Path = $file.FullName
            Line = $line
            Binding = $bindingPath
        }
    }
}

$findings = @($findings)
if ($findings.Count -eq 0) {
    Write-Host "Money input scan passed: no money-like TextBox bindings without thousand separators."
    exit 0
}

$findings | Sort-Object Path, Line | Format-Table -AutoSize
Write-Warning "Found $($findings.Count) money-like TextBox binding(s) without AppMoneyTextBox or ThousandSeparatorBehavior."

if ($FailOnFinding) {
    exit 1
}
