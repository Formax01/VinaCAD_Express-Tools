$assetDirectory = Join-Path $PSScriptRoot 'Door Assets'
$files = @(Get-ChildItem -LiteralPath $assetDirectory -Filter 'DR_A*.dwg' -File)
if ($files.Count -eq 0) { throw 'Không tìm thấy door asset DWG.' }

foreach ($file in $files) {
    $stream = [System.IO.File]::OpenRead($file.FullName)
    try {
        $header = New-Object byte[] 6
        if ($stream.Read($header, 0, 6) -ne 6 -or [Text.Encoding]::ASCII.GetString($header) -notmatch '^AC10') {
            throw "Door asset không phải DWG hợp lệ: $($file.Name)"
        }
    }
    finally {
        $stream.Dispose()
    }
}

Write-Output "Door assets OK: $($files.Count) DWG"
