param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\Lamie.API\Data\AdministrativeUnits')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web

function Get-CellText($cell) {
    return (($cell.Range.Text -replace '[\r\a]', '').Trim()).Normalize([Text.NormalizationForm]::FormC)
}

function Get-BareName([string]$fullName) {
    return ($fullName -replace '(?i)^(Tỉnh|Thành phố|Quận|Huyện|Thị xã|Xã|Phường|Thị trấn|Đặc khu)\s+', '').Trim()
}

function Get-UnitType([string]$fullName, [int]$level) {
    if ($level -eq 1) {
        if ($fullName.StartsWith('Thành phố ', [StringComparison]::OrdinalIgnoreCase)) { return 'Municipality' }
        return 'Province'
    }
    if ($fullName.StartsWith('Quận ', [StringComparison]::OrdinalIgnoreCase)) { return 'UrbanDistrict' }
    if ($fullName.StartsWith('Huyện ', [StringComparison]::OrdinalIgnoreCase)) { return 'District' }
    if ($fullName.StartsWith('Thị xã ', [StringComparison]::OrdinalIgnoreCase)) { return 'Town' }
    if ($fullName.StartsWith('Thành phố ', [StringComparison]::OrdinalIgnoreCase)) { return 'ProvincialCity' }
    if ($fullName.StartsWith('Phường ', [StringComparison]::OrdinalIgnoreCase)) { return 'Ward' }
    if ($fullName.StartsWith('Thị trấn ', [StringComparison]::OrdinalIgnoreCase)) { return 'Township' }
    if ($fullName.StartsWith('Đặc khu ', [StringComparison]::OrdinalIgnoreCase)) { return 'SpecialZone' }
    return 'Commune'
}

function Write-JsonFile([string]$path, $value) {
    $json = $value | ConvertTo-Json -Depth 12
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
    [IO.File]::WriteAllText($path, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Get-DownloadLinks([string]$pageUrl, [string]$extension) {
    $page = Invoke-WebRequest -UseBasicParsing $pageUrl -TimeoutSec 60
    return @($page.Links |
        Where-Object { $_.href -match "\.$extension(?:$|[?&])" } |
        ForEach-Object { [System.Web.HttpUtility]::HtmlDecode($_.href) })
}

function Read-SqlRows([string]$path) {
    $section = $null
    $result = @{ provinces = @(); districts = @(); communes = @() }
    foreach ($line in Get-Content -Encoding utf8 $path) {
        if ($line.StartsWith('INSERT INTO `1_devvn_tinhthanhpho`')) { $section = 'provinces'; continue }
        if ($line.StartsWith('INSERT INTO `2_devvn_quanhuyen`')) { $section = 'districts'; continue }
        if ($line.StartsWith('INSERT INTO `3_devvn_xaphuongthitran`')) { $section = 'communes'; continue }
        if ($null -eq $section -or -not $line.StartsWith("('")) { continue }
        if ($line -notmatch "^\('(?<a>(?:\\'|[^'])*)',\s*'(?<b>(?:\\'|[^'])*)',\s*'(?<c>(?:\\'|[^'])*)',\s*'(?<d>(?:\\'|[^'])*)'\)[,;]$") {
            throw "Cannot parse legacy SQL row: $line"
        }
        $result[$section] += [pscustomobject]@{
            Code = $Matches.a.Replace("\'", "'")
            Name = $Matches.b.Replace("\'", "'").Normalize([Text.NormalizationForm]::FormC)
            Type = $Matches.c.Replace("\'", "'")
            Parent = $Matches.d
        }
    }
    return $result
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) 'lamie-administrative-data-build'
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

$decisionPage = 'https://congbao.chinhphu.vn/van-ban/quyet-dinh-so-19-2025-qd-ttg-45430/57438.htm'
$docLinks = Get-DownloadLinks $decisionPage 'doc'
if ($docLinks.Count -ne 2) { throw "Expected two official DOC parts, found $($docLinks.Count)." }
$currentDocs = @()
for ($index = 0; $index -lt $docLinks.Count; $index++) {
    $path = Join-Path $temporaryRoot "19-2025-QD-TTg-part-$($index + 1).doc"
    Invoke-WebRequest -UseBasicParsing $docLinks[$index] -OutFile $path -TimeoutSec 60
    $currentDocs += $path
}

$signedDecisionPath = Join-Path $temporaryRoot '19-2025-QD-TTg.signed.pdf'
$signedDecisionUrl = 'https://datafiles.chinhphu.vn/cpp/files/vbpq/2025/7/19ttg.signed.pdf'
Invoke-WebRequest -UseBasicParsing $signedDecisionUrl -OutFile $signedDecisionPath -TimeoutSec 60
$currentChecksum = (Get-FileHash -Algorithm SHA256 $signedDecisionPath).Hash

$currentUnits = [Collections.Generic.List[object]]::new()
$word = New-Object -ComObject Word.Application
$word.Visible = $false
try {
    $firstDocument = $word.Documents.Open($currentDocs[0], $false, $true)
    try {
        $provinceTable = $firstDocument.Tables.Item(2)
        for ($rowIndex = 2; $rowIndex -le $provinceTable.Rows.Count; $rowIndex++) {
            $code = Get-CellText $provinceTable.Cell($rowIndex, 2)
            $fullName = Get-CellText $provinceTable.Cell($rowIndex, 3)
            $currentUnits.Add([ordered]@{
                code = $code
                name = Get-BareName $fullName
                fullName = $fullName
                unitType = Get-UnitType $fullName 1
                hierarchyLevel = 1
                parentCode = $null
                isActive = $true
                sortOrder = $rowIndex - 2
            })
        }
    }
    finally { $firstDocument.Close($false) }

    foreach ($path in $currentDocs) {
        $document = $word.Documents.Open($path, $false, $true)
        try {
            $firstTable = if ($path -eq $currentDocs[0]) { 3 } else { 1 }
            for ($tableIndex = $firstTable; $tableIndex -le $document.Tables.Count; $tableIndex++) {
                $table = $document.Tables.Item($tableIndex)
                $start = $table.Range.Start
                $before = ($document.Range([Math]::Max(0, $start - 220), $start).Text -replace '[\r\a]', ' ' -replace '\s+', ' ')
                if ($before -notmatch '(?<provinceCode>\d{2})\.\s+(?:THÀNH PHỐ|TỈNH)\s+[^\(]+\(Tổng số đơn vị hành chính cấp xã') {
                    throw "Cannot find province heading for table $tableIndex in $path"
                }
                $provinceCode = $Matches.provinceCode
                for ($rowIndex = 2; $rowIndex -le $table.Rows.Count; $rowIndex++) {
                    $code = Get-CellText $table.Cell($rowIndex, 1)
                    $fullName = Get-CellText $table.Cell($rowIndex, 2)
                    $currentUnits.Add([ordered]@{
                        code = $code
                        name = Get-BareName $fullName
                        fullName = $fullName
                        unitType = Get-UnitType $fullName 2
                        hierarchyLevel = 2
                        parentCode = $provinceCode
                        isActive = $true
                        sortOrder = $rowIndex - 2
                    })
                }
            }
        }
        finally { $document.Close($false) }
    }
}
finally { $word.Quit() }

$currentDataset = [ordered]@{
    datasetVersion = 'CURRENT-2025-07-01-QD19'
    scheme = 'Current'
    effectiveFrom = '2025-07-01'
    effectiveTo = $null
    sourceDocument = '19/2025/QĐ-TTg'
    sourceReference = $signedDecisionUrl
    sourceChecksum = "SHA256:$currentChecksum"
    units = $currentUnits
}
Write-JsonFile (Join-Path $OutputRoot 'administrative-units.current.json') $currentDataset

$legacySqlPath = Join-Path $temporaryRoot 'legacy-address-2025-03-06.sql'
$legacyMirrorId = '19N4fF_RbMgumbKEqqFac6WKzaSN5XMGq'
Invoke-WebRequest -UseBasicParsing "https://drive.usercontent.google.com/download?id=$legacyMirrorId&export=download&confirm=t" -OutFile $legacySqlPath -TimeoutSec 60
$legacyMirrorChecksum = (Get-FileHash -Algorithm SHA256 $legacySqlPath).Hash
$legacyRows = Read-SqlRows $legacySqlPath
if ($legacyRows.provinces.Count -ne 63 -or $legacyRows.districts.Count -ne 696 -or $legacyRows.communes.Count -ne 10035) {
    throw "Legacy counts are invalid: $($legacyRows.provinces.Count)/$($legacyRows.districts.Count)/$($legacyRows.communes.Count)"
}

$legacyUnits = [Collections.Generic.List[object]]::new()
foreach ($row in $legacyRows.provinces) {
    $fullName = if ($row.Code -eq '46') { 'Thành phố Huế' } else { $row.Name }
    $legacyUnits.Add([ordered]@{
        code = $row.Code
        name = Get-BareName $fullName
        fullName = $fullName
        unitType = Get-UnitType $fullName 1
        hierarchyLevel = 1
        parentCode = $null
        isActive = $true
        sortOrder = [int]$row.Code
    })
}
foreach ($row in $legacyRows.districts) {
    $legacyUnits.Add([ordered]@{
        code = $row.Code
        name = Get-BareName $row.Name
        fullName = $row.Name
        unitType = Get-UnitType $row.Name 2
        hierarchyLevel = 2
        parentCode = $row.Parent
        isActive = $true
        sortOrder = [int]$row.Code
    })
}
foreach ($row in $legacyRows.communes) {
    $legacyUnits.Add([ordered]@{
        code = $row.Code
        name = Get-BareName $row.Name
        fullName = $row.Name
        unitType = Get-UnitType $row.Name 3
        hierarchyLevel = 3
        parentCode = $row.Parent
        isActive = $true
        sortOrder = [int]$row.Code
    })
}

$legacyDataset = [ordered]@{
    datasetVersion = 'LEGACY-2025-06-30-CONSOLIDATED'
    scheme = 'Legacy'
    effectiveFrom = '2025-01-01'
    effectiveTo = '2025-06-30'
    sourceDocument = '124/2004/QĐ-TTg; 50 Nghị quyết sắp xếp ĐVHC 2023-2025; 175/2024/QH15'
    sourceReference = 'https://moha.gov.vn/tin-tuc/sap-xep-don-vi-hanh-chinh-cac-thanh-pho-truc-thuoc---oid56647'
    sourceChecksum = "TECHNICAL_MIRROR_SHA256:$legacyMirrorChecksum"
    units = $legacyUnits
}
Write-JsonFile (Join-Path $OutputRoot 'administrative-units.legacy.json') $legacyDataset

$legacyCommunesByCode = @{}
foreach ($unit in $legacyUnits | Where-Object { $_.hierarchyLevel -eq 3 }) { $legacyCommunesByCode[$unit.code] = $unit }
$transitions = [Collections.Generic.List[object]]::new()
$transitionSource = 'https://xaydungchinhsach.chinhphu.vn/toan-van-34-nghi-quyet-cua-ubtvqh-ve-sap-xep-cac-don-vi-hanh-chinh-cap-xa-119250616215143373.htm'
$resolutionByCurrentProvinceCode = @{
    '01'='1656/NQ-UBTVQH15'; '04'='1657/NQ-UBTVQH15'; '08'='1684/NQ-UBTVQH15'
    '11'='1661/NQ-UBTVQH15'; '12'='1670/NQ-UBTVQH15'; '14'='1681/NQ-UBTVQH15'
    '15'='1673/NQ-UBTVQH15'; '19'='1683/NQ-UBTVQH15'; '20'='1672/NQ-UBTVQH15'
    '22'='1679/NQ-UBTVQH15'; '24'='1658/NQ-UBTVQH15'; '25'='1676/NQ-UBTVQH15'
    '31'='1669/NQ-UBTVQH15'; '33'='1666/NQ-UBTVQH15'; '37'='1674/NQ-UBTVQH15'
    '38'='1686/NQ-UBTVQH15'; '40'='1678/NQ-UBTVQH15'; '42'='1665/NQ-UBTVQH15'
    '44'='1680/NQ-UBTVQH15'; '46'='1675/NQ-UBTVQH15'; '48'='1659/NQ-UBTVQH15'
    '51'='1677/NQ-UBTVQH15'; '52'='1664/NQ-UBTVQH15'; '56'='1667/NQ-UBTVQH15'
    '66'='1660/NQ-UBTVQH15'; '68'='1671/NQ-UBTVQH15'; '75'='1662/NQ-UBTVQH15'
    '79'='1685/NQ-UBTVQH15'; '80'='1682/NQ-UBTVQH15'; '82'='1663/NQ-UBTVQH15'
    '86'='1687/NQ-UBTVQH15'; '91'='1654/NQ-UBTVQH15'; '92'='1668/NQ-UBTVQH15'
    '96'='1655/NQ-UBTVQH15'
}
foreach ($unit in $currentUnits | Where-Object { $_.hierarchyLevel -eq 2 }) {
    if (-not $legacyCommunesByCode.ContainsKey($unit.code)) { continue }
    if (-not $resolutionByCurrentProvinceCode.ContainsKey($unit.parentCode)) {
        throw "No official 2025 resolution is mapped for current province code $($unit.parentCode)"
    }
    $legacy = $legacyCommunesByCode[$unit.code]
    $transitions.Add([ordered]@{
        legacyUnitCode = $legacy.code
        currentUnitCode = $unit.code
        transitionType = 'Partial'
        sourceDocument = "$($resolutionByCurrentProvinceCode[$unit.parentCode]); 19/2025/QĐ-TTg"
        sourceReference = $transitionSource
        effectiveDate = '2025-07-01'
        note = 'Conservative code-retention suggestion. Boundary equivalence is not asserted; confirm against the province-specific resolution.'
    })
}
$transitionDataset = [ordered]@{
    datasetVersion = 'TRANSITIONS-2025-07-01-CONSERVATIVE'
    transitions = $transitions
}
Write-JsonFile (Join-Path $OutputRoot 'administrative-unit-transitions.json') $transitionDataset

Write-Output "CURRENT provinces=$(@($currentUnits | Where-Object hierarchyLevel -eq 1).Count) communes=$(@($currentUnits | Where-Object hierarchyLevel -eq 2).Count)"
Write-Output "LEGACY provinces=$($legacyRows.provinces.Count) districts=$($legacyRows.districts.Count) communes=$($legacyRows.communes.Count)"
Write-Output "TRANSITIONS count=$($transitions.Count)"
