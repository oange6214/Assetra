param(
    [string]$Root = ".\Assetra.WPF\DesignSystem",
    [switch]$FailOnExternalBasedOn
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Root)) {
    throw "Root path not found: $Root"
}

$files = Get-ChildItem -LiteralPath $Root -Recurse -Include *.xaml -File
$results = foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $localKeys = [regex]::Matches($text, 'x:Key="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    $basedOnMatches = [regex]::Matches($text, 'BasedOn="\{StaticResource ([^\}]+)\}"')

    foreach ($match in $basedOnMatches) {
        $key = $match.Groups[1].Value
        [pscustomobject]@{
            Path = $file.FullName
            Resource = $key
            Kind = "BasedOn"
            Local = $localKeys -contains $key
        }
    }
}

$externalBasedOn = @($results | Where-Object { $_.Kind -eq "BasedOn" -and -not $_.Local })

if ($results) {
    $results | Sort-Object Path, Resource | Format-Table -AutoSize
}
else {
    Write-Host "No StaticResource BasedOn references found."
}

if ($externalBasedOn.Count -gt 0) {
    Write-Warning "Found $($externalBasedOn.Count) BasedOn references to keys not defined in the same dictionary."
    $externalBasedOn | Sort-Object Path, Resource | Format-Table -AutoSize

    if ($FailOnExternalBasedOn) {
        exit 1
    }
}
else {
    Write-Host "Resource scan passed: all BasedOn StaticResource references are local to their dictionary."
}
