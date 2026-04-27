param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [string]$OutputPath
)

if (-not $OutputPath) {
    $dir  = Split-Path -Parent $InputPath
    $name = [IO.Path]::GetFileNameWithoutExtension($InputPath) -replace '-utf8$',''
    $ext  = [IO.Path]::GetExtension($InputPath)
    $OutputPath = Join-Path $dir "$name-big5$ext"
}

[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
$big5 = [Text.Encoding]::GetEncoding('big5')
$content = Get-Content -Path $InputPath -Raw -Encoding UTF8
[IO.File]::WriteAllText($OutputPath, $content, $big5)
Write-Host "Wrote Big5: $OutputPath"
