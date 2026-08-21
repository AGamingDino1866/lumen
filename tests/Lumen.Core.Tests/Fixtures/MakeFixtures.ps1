# Generates sample.pdf: a 3-page A4 PDF used by PdfRenderServiceTests.
# The file is committed, so this script only needs running if the fixture must change.
# Writing raw PDF avoids adding a PDF-authoring dependency to the test project.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function New-ContentStream([string]$text) {
    # A minimal content stream: begin text, pick Helvetica 24pt, position, show string, end text.
    return "BT /F1 24 Tf 72 720 Td ($text) Tj ET"
}

$pageTexts = @(
    'Lumen fixture page one',
    'Lumen fixture page two',
    '.'                       # near-blank third page
)

$objects = New-Object System.Collections.Generic.List[string]

# 1: Catalog, 2: Pages, 3: Font, then per page: page object + content stream.
$pageCount = $pageTexts.Count
$firstPageObj = 4
$kids = (0..($pageCount - 1) | ForEach-Object { "$($firstPageObj + $_ * 2) 0 R" }) -join ' '

$objects.Add("<< /Type /Catalog /Pages 2 0 R >>")
$objects.Add("<< /Type /Pages /Kids [$kids] /Count $pageCount >>")
$objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")

for ($i = 0; $i -lt $pageCount; $i++) {
    $contentObj = $firstPageObj + $i * 2 + 1
    $objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents $contentObj 0 R >>")
    $stream = New-ContentStream $pageTexts[$i]
    $objects.Add("<< /Length $($stream.Length) >>`nstream`n$stream`nendstream")
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("%PDF-1.4`n")

$offsets = New-Object System.Collections.Generic.List[int]
for ($i = 0; $i -lt $objects.Count; $i++) {
    $offsets.Add($sb.Length)
    [void]$sb.Append("$($i + 1) 0 obj`n$($objects[$i])`nendobj`n")
}

$xrefPos = $sb.Length
[void]$sb.Append("xref`n0 $($objects.Count + 1)`n")
[void]$sb.Append("0000000000 65535 f `n")
foreach ($o in $offsets) {
    [void]$sb.Append(("{0:D10} 00000 n `n" -f $o))
}
[void]$sb.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xrefPos`n%%EOF")

$bytes = [System.Text.Encoding]::ASCII.GetBytes($sb.ToString())
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'sample.pdf'), $bytes)

Write-Output ("sample.pdf written: {0} bytes, {1} pages" -f $bytes.Length, $pageCount)
