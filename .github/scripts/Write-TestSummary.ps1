param(
    [Parameter(Mandatory)][string] $ResultsDirectory,
    [Parameter(Mandatory)][string] $SummaryPath
)

$ErrorActionPreference = 'Stop'
$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add('## Backend test results')
$summary.Add('Download the **backend-test-results** artifact on this workflow run for the complete TRX results.')
$summary.Add('')

$files = if (Test-Path -LiteralPath $ResultsDirectory) {
    @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
} else { @() }

if ($files.Count -eq 0) {
    $summary.Add('No TRX file was produced. Check the build/test-host step for startup or infrastructure errors.')
    Write-Warning 'No backend TRX results found.'
}

foreach ($file in $files) {
    try {
        [xml] $document = Get-Content -LiteralPath $file.FullName -Raw
        $namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
        $namespaces.AddNamespace('t', $document.DocumentElement.NamespaceURI)
        $counters = $document.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $namespaces)
        if ($null -eq $counters) { throw 'TRX counters are missing.' }
        $summary.Add("Total: $($counters.total); passed: $($counters.passed); failed: $($counters.failed); not executed: $($counters.notExecuted).")

        $failed = @($document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult[@outcome="Failed"]', $namespaces))
        foreach ($test in ($failed | Select-Object -First 10)) {
            $name = [System.Net.WebUtility]::HtmlEncode($test.GetAttribute('testName'))
            if ($name.Length -gt 1000) { $name = $name.Substring(0, 1000) + '…' }
            $message = $test.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $namespaces)
            $stack = $test.SelectSingleNode('t:Output/t:ErrorInfo/t:StackTrace', $namespaces)
            $details = @(@($message.InnerText, $stack.InnerText) -join "`n" -split '\r?\n')
            $excerpt = @($details | Select-Object -First 50 | ForEach-Object {
                if ($_.Length -gt 1000) { $_.Substring(0, 1000) + '…' } else { $_ }
            }) -join "`n"
            $summary.Add("<details><summary>$name</summary><pre>$([System.Net.WebUtility]::HtmlEncode($excerpt))</pre></details>")
            if ($details.Count -gt 50) { $summary.Add("$($details.Count - 50) additional lines omitted; see the TRX artifact.") }
        }
        if ($failed.Count -gt 10) { $summary.Add("$($failed.Count - 10) additional failed tests omitted; see the TRX artifact.") }
        if ($document.TestRun.ResultSummary.outcome -eq 'Failed' -and $failed.Count -eq 0) {
            $summary.Add('The test run failed without an individual failed test result. Check the TRX run diagnostics and test-host logs.')
        }
    }
    catch {
        $summary.Add("Could not read TRX results from $([System.Net.WebUtility]::HtmlEncode($file.Name)); check the artifact and test-host logs.")
        Write-Warning "Could not read TRX results from $($file.Name)."
    }
}

Add-Content -LiteralPath $SummaryPath -Value ($summary -join "`n") -Encoding utf8
