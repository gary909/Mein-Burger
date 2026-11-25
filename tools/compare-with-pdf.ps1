param(
    [Parameter(Mandatory = $true)][string]$PdfText,
    [Parameter(Mandatory = $true)][string]$Dataset,
    [string]$OutCsv = '.\tools\.cache\dataset-vs-pdf.csv'
)

trap {
    $message = "ERROR line $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line) :: $($_.Exception.Message)"
    Add-Content -LiteralPath (Join-Path $env:TEMP 'mb-compare-error.log') -Value $message -Encoding utf8
    exit 1
}

$ErrorActionPreference = 'Stop'

$outDir = Split-Path -Parent $OutCsv
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}
$stateCodes = @{
    'Baden-Württemberg' = 'BW'; 'Bayern' = 'BY'; 'Berlin' = 'BE'; 'Brandenburg' = 'BB'
    'Bremen' = 'HB'; 'Hamburg' = 'HH'; 'Hessen' = 'HE'; 'Mecklenburg-Vorpommern' = 'MV'
    'Niedersachsen' = 'NI'; 'Nordrhein-Westfalen' = 'NW'; 'Rheinland-Pfalz' = 'RP'
    'Saarland' = 'SL'; 'Sachsen' = 'SN'; 'Sachsen-Anhalt' = 'ST'
    'Schleswig-Holstein' = 'SH'; 'Thüringen' = 'TH'
}
$codeToSlug = @{
    'BW' = 'baden-wuerttemberg'; 'BY' = 'bayern'; 'BE' = 'berlin'; 'BB' = 'brandenburg'
    'HB' = 'bremen'; 'HH' = 'hamburg'; 'HE' = 'hessen'; 'MV' = 'mecklenburg-vorpommern'
    'NI' = 'niedersachsen'; 'NW' = 'nordrhein-westfalen'; 'RP' = 'rheinland-pfalz'
    'SL' = 'saarland'; 'SN' = 'sachsen'; 'ST' = 'sachsen-anhalt'
    'SH' = 'schleswig-holstein'; 'TH' = 'thueringen'
}

$optionMarkers = @([char]0xF0A3, [char]0x25A1, [char]0x2610)
$lines = Get-Content -LiteralPath $PdfText -Encoding UTF8
$part = 'I'
$state = $null
$current = $null
$pdfQuestions = New-Object System.Collections.Generic.List[object]

foreach ($rawLine in $lines) {
    $line = $rawLine.Trim()
    if ($line -match '^Fragen für das Bundesland (.+)$') {
        $state = $matches[1]; $part = 'II'; $current = $null; continue
    }
    if ($line -eq 'Teil I') { $part = 'I'; $state = $null; $current = $null; continue }
    if ($line -eq 'Teil II') { $part = 'II'; $current = $null; continue }
    if ($line -match '^Seite \d+ von \d+$') { continue }
    if ($line -match '^Aufgabe (\d+)$') {
        $current = [pscustomobject]@{
            Part = $part; State = $state; No = [int]$matches[1]; Text = ''
            Options = New-Object System.Collections.Generic.List[string]
        }
        $pdfQuestions.Add($current); continue
    }
    if (-not $current -or $line.Length -eq 0) { continue }
    if ($line -match '^Bild \d(\s+Bild \d)*$') { continue }
    if ($line.StartsWith('©')) { continue }
    if ($optionMarkers -contains $line[0]) {
        $current.Options.Add($line.Substring(1).Trim())
    }
    elseif ($current.Options.Count -gt 0) {
        $index = $current.Options.Count - 1
        $current.Options[$index] = ($current.Options[$index] + ' ' + $line).Trim()
    }
    else {
        $current.Text = ($current.Text + ' ' + $line).Trim()
    }
}

$data = Get-Content -Raw -LiteralPath $Dataset -Encoding UTF8 | ConvertFrom-Json
$federalById = @{}
foreach ($question in $data.general) { $federalById[[int]$question.id] = $question }

function Get-Tokens([string]$value) {
    $normalized = ([string]$value).Normalize([System.Text.NormalizationForm]::FormC).ToLowerInvariant().Replace('ß', 'ss')
    $matches = [regex]::Matches($normalized, '[\p{L}\p{Nd}]+')
    $tokens = @($matches | ForEach-Object { $_.Value })
    [Array]::Sort($tokens)
    return ($tokens -join ' ')
}

function Get-OptionSignature($options) {
    return (($options | ForEach-Object { Get-Tokens $_ } | Sort-Object) -join '|')
}

$mismatches = New-Object System.Collections.Generic.List[object]
$counts = @{ 'I' = 0; 'II' = 0 }

foreach ($question in $pdfQuestions) {
    $counts[$question.Part] = $counts[$question.Part] + 1
    $datasetId = $null
    $datasetQuestion = $null

    if ($question.Part -eq 'I') {
        $datasetId = ('B{0:000}' -f $question.No)
        if ($federalById.ContainsKey($question.No)) { $datasetQuestion = $federalById[$question.No] }
    }
    else {
        if (-not $question.State) {
            $mismatches.Add([pscustomobject]@{
                part = $question.Part; state = $question.State; no = $question.No; id = "NO-STATE-$($question.No)"
                issue = 'missing state header'; pdfText = $question.Text; datasetText = ''
                pdfOptions = ($question.Options -join ' || '); datasetOptions = ''
            })
            continue
        }
        $code = $stateCodes[$question.State]
        $datasetId = if ($code) { ('{0}{1:00}' -f $code, $question.No) } else { "UNKNOWN-$($question.No)" }
        if (-not $code) {
            $mismatches.Add([pscustomobject]@{
                part = $question.Part; state = $question.State; no = $question.No; id = $datasetId
                issue = 'unknown state'; pdfText = $question.Text; datasetText = ''
                pdfOptions = ($question.Options -join ' || '); datasetOptions = ''
            })
            continue
        }
        $slug = $codeToSlug[$code]
        if ($slug) {
            $stateQuestions = $data.states.$slug.questions
            if ($question.No -le $stateQuestions.Count) { $datasetQuestion = $stateQuestions[$question.No - 1] }
        }
    }

    if (-not $datasetQuestion) {
        $mismatches.Add([pscustomobject]@{
            part = $question.Part; state = $question.State; no = $question.No; id = $datasetId
            issue = 'missing in dataset'; pdfText = $question.Text; datasetText = ''
            pdfOptions = ($question.Options -join ' || '); datasetOptions = ''
        })
        continue
    }

    if ($question.Options.Count -ne 4) {
        $mismatches.Add([pscustomobject]@{
            part = $question.Part; state = $question.State; no = $question.No; id = $datasetId
            issue = "option count $($question.Options.Count)"; pdfText = $question.Text
            datasetText = $datasetQuestion.de
            pdfOptions = ($question.Options -join ' || ')
            datasetOptions = (($datasetQuestion.options | ForEach-Object { $_.de }) -join ' || ')
        })
    }

    $textMatches = (Get-Tokens $question.Text) -eq (Get-Tokens $datasetQuestion.de)
    $pdfSignature = Get-OptionSignature $question.Options
    $datasetSignature = Get-OptionSignature ($datasetQuestion.options | ForEach-Object { $_.de })
    $optionsMatch = $pdfSignature -eq $datasetSignature

    if (-not $textMatches -or -not $optionsMatch) {
        $issue = @()
        if (-not $textMatches) { $issue += 'question text' }
        if (-not $optionsMatch) { $issue += 'options' }
        $mismatches.Add([pscustomobject]@{
            part = $question.Part; state = $question.State; no = $question.No; id = $datasetId
            issue = ($issue -join ' + '); pdfText = $question.Text; datasetText = $datasetQuestion.de
            pdfOptions = ($question.Options -join ' || ')
            datasetOptions = (($datasetQuestion.options | ForEach-Object { $_.de }) -join ' || ')
        })
    }
}

$mismatches | Export-Csv -LiteralPath $OutCsv -NoTypeInformation -Delimiter ';' -Encoding UTF8

Write-Output ("PDF questions parsed: federal={0}, state={1}, total={2}" -f $counts['I'], $counts['II'], $pdfQuestions.Count)
Write-Output ("Mismatches: {0} -> {1}" -f $mismatches.Count, $OutCsv)
$mismatches | Select-Object -First 12 | Format-Table -AutoSize -Wrap



