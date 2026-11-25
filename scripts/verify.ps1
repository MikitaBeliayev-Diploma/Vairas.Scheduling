param([switch]$SkipIntegration,[string]$Configuration='Release')
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $run=Join-Path 'evidence' ('run-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $run | Out-Null
    $properties=@()
    if($SkipIntegration){$properties=@('-p:EnableServiceIntegrationTests=false')}
    dotnet restore Vairas.Scheduling.slnx @properties
    if($LASTEXITCODE){throw 'Restore/security audit failed.'}
    dotnet tool restore
    if($LASTEXITCODE){throw 'Tool restore failed.'}
    dotnet build Vairas.Scheduling.slnx -c $Configuration --no-restore @properties --nologo
    if($LASTEXITCODE){throw 'Build failed.'}
    dotnet ef migrations has-pending-model-changes --project Vairas.Scheduling.Persistance --startup-project Vairas.Scheduling.Persistance --configuration $Configuration --no-build
    if($LASTEXITCODE){throw 'EF model differs from the migration snapshot.'}
    $unit=Join-Path $run 'unit'
    dotnet test Vairas.Scheduling.Tests -c $Configuration --no-build --no-restore @properties --filter 'TestCategory=Unit' --settings coverage.runsettings --collect:'XPlat Code Coverage' --logger 'trx;LogFileName=unit.trx' --results-directory $unit --nologo
    if($LASTEXITCODE){throw 'Unit tests failed.'}
    $reports=@(Get-ChildItem -LiteralPath $unit -Directory | ForEach-Object {Get-ChildItem -LiteralPath $_.FullName -File -Filter coverage.cobertura.xml})
    if($reports.Count -ne 1){throw 'One fresh unit coverage report is required.'}
    [xml]$coverage=Get-Content -Raw -LiteralPath $reports[0].FullName
    $metrics=@()
    foreach($assembly in @('Vairas.Scheduling.Business','Vairas.Scheduling.Domain')){
        $package=@($coverage.coverage.packages.package | Where-Object name -eq $assembly)
        if($package.Count -ne 1){throw "Coverage missing for $assembly."}
        $rate=[double]::Parse($package[0].'line-rate',[Globalization.CultureInfo]::InvariantCulture)
        if($rate -lt 0.70){throw "$assembly unit line coverage below 70%: $rate"}
        $metrics+= [ordered]@{assembly=$assembly;unitLineCoverage=$rate;branchCoverage=$package[0].'branch-rate'}
    }
    [xml]$unitResult=Get-Content -Raw -LiteralPath (Join-Path $unit 'unit.trx')
    if([int]$unitResult.TestRun.ResultSummary.Counters.executed -lt 1){throw 'No unit tests executed.'}
    $integrationResult=$null
    if(!$SkipIntegration){
        $integration=Join-Path $run 'integration'
        dotnet test Vairas.Scheduling.Tests -c $Configuration --no-build --no-restore --filter 'TestCategory=Integration' --logger 'trx;LogFileName=integration.trx' --results-directory $integration --nologo
        if($LASTEXITCODE){throw 'PostgreSQL/HTTP integration tests failed.'}
        [xml]$integrationResult=Get-Content -Raw -LiteralPath (Join-Path $integration 'integration.trx')
        if([int]$integrationResult.TestRun.ResultSummary.Counters.executed -lt 1){throw 'No integration tests executed.'}
    }
    $table=($metrics | ForEach-Object { '<tr><td>'+[System.Net.WebUtility]::HtmlEncode($_.assembly)+'</td><td>'+('{0:P2}' -f $_.unitLineCoverage)+'</td></tr>' }) -join ''
    '<!doctype html><html lang="en"><meta charset="utf-8"><title>Scheduling unit coverage</title><h1>Scheduling unit coverage</h1><p>Measured by Coverlet; unit tests only. Minimum line coverage per assembly: 70%.</p><table><tr><th>Assembly</th><th>Line coverage</th></tr>'+ $table+'</table></html>' | Set-Content -Encoding UTF8 (Join-Path $run 'coverage.html')
    function Counters($node){[ordered]@{executed=[int]$node.executed;passed=[int]$node.passed;failed=[int]$node.failed;skipped=[int]$node.notExecuted}}
    $summary=[ordered]@{checkedAt=[DateTimeOffset]::UtcNow.ToString('O');sdk=(dotnet --version);coverage=$metrics;unit=(Counters $unitResult.TestRun.ResultSummary.Counters);integration=if($SkipIntegration){'NOT RUN'}else{Counters $integrationResult.TestRun.ResultSummary.Counters};report=$reports[0].FullName;production='Not deployed'}
    $summary | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $run 'summary.json')
    Write-Output ($summary | ConvertTo-Json -Depth 8)
} finally {Pop-Location}
