#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ConfigurationPath, [Parameter(Mandatory)] [string] $PricingInputsPath,
      [Parameter(Mandatory)] [string] $OutputPath)
. (Join-Path $PSScriptRoot 'Validation.ps1')
$config = Read-TaskManagerConfiguration $ConfigurationPath
if ((Get-TaskManagerField $config 'monthlyBudgetCurrency' '') -cne 'USD') { throw 'Retail USD budget comparison requires an explicitly verified USD billing budget; another currency needs a reviewed conversion contract.' }
$inputs = Get-Content -LiteralPath $PricingInputsPath -Raw | ConvertFrom-Json -AsHashtable -Depth 50
$requiredServices = @('ContainerApps', 'SqlDatabase', 'Registry', 'ServiceBus', 'SignalR', 'OpenAI', 'CommunicationEmail', 'KeyVault', 'BlobStorage', 'Monitor', 'PrivateEndpoints', 'PrivateDns', 'Bandwidth')
$rows = @()
foreach ($line in $inputs.lines) {
    if ($line.service -notin $requiredServices -or $line.armRegionName -cnotmatch '^[a-z0-9]{2,30}$' -or $line.meterId -cnotmatch '^[a-f0-9-]{36}$' -or
        $line.quantity -lt 0 -or -not [double]::IsFinite([double] $line.quantity) -or [string]::IsNullOrWhiteSpace($line.quantityFormula)) { throw 'Every price line requires a known service, concrete region, exact retail meter ID and explicit nonnegative quantity/formula.' }
    $filter = "armRegionName eq '$($line.armRegionName)' and meterId eq '$($line.meterId)' and priceType eq 'Consumption'"
    $uri = 'https://prices.azure.com/api/retail/prices?currencyCode=USD&api-version=2023-01-01-preview&$filter=' + [Uri]::EscapeDataString($filter)
    $meters = @(); $pages = 0
    do {
        if (++$pages -gt 20 -or ([Uri] $uri).Scheme -ne 'https' -or ([Uri] $uri).Host -cne 'prices.azure.com' -or ([Uri] $uri).AbsolutePath -cne '/api/retail/prices') { throw 'Retail pagination exceeded its bounded trusted endpoint.' }
        $page = Invoke-RestMethod -Uri $uri
        $meters += @($page.Items | Where-Object { $_.meterId -ceq $line.meterId -and $_.armRegionName -ceq $line.armRegionName -and $_.type -ceq 'Consumption' -and $_.currencyCode -ceq 'USD' -and $_.tierMinimumUnits -eq 0 -and [DateTimeOffset]::Parse($_.effectiveStartDate) -le [DateTimeOffset]::UtcNow })
        $uri = $page.NextPageLink
    } while ($uri)
    if (-not $meters.Count) { throw 'No current exact retail price matched; zero-cost fallback is prohibited.' }
    $latest = @($meters | Sort-Object { [DateTimeOffset]::Parse($_.effectiveStartDate) } -Descending)
    $same = @($latest | Where-Object { $_.effectiveStartDate -ceq $latest[0].effectiveStartDate })
    if ($same.Count -ne 1 -or $same[0].retailPrice -lt 0) { throw 'Retail meter selection is ambiguous or invalid.' }
    $meter = $same[0]
    if ($line.expectedUnitOfMeasure -cne $meter.unitOfMeasure) { throw 'Quantity units differ from the actual retail meter.' }
    $rows += @{ service = $line.service; region = $line.armRegionName; meterId = $meter.meterId; meterName = $meter.meterName; skuName = $meter.skuName;
        unitOfMeasure = $meter.unitOfMeasure; effectiveStartDate = $meter.effectiveStartDate; unitPriceUsd = $meter.retailPrice; quantity = $line.quantity;
        quantityFormula = $line.quantityFormula; monthlyEstimateUsd = [Math]::Round([decimal] $meter.retailPrice * [decimal] $line.quantity, 4); sharedResource = [bool] (Get-TaskManagerField $line 'sharedResource' $false) }
}
$missing = @($requiredServices | Where-Object { $_ -notin @($rows | ForEach-Object { $_.service }) })
$total = ($rows | Measure-Object monthlyEstimateUsd -Sum).Sum
$report = @{ source = 'Azure Retail Prices API'; fetchedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); currency = 'USD'; apiVersion = '2023-01-01-preview';
    configuredRegion = $config.foundation.location; rows = $rows; knownSubtotalUsd = $total; missingServiceEstimates = $missing; completeServiceCoverage = $missing.Count -eq 0;
    configuredMonthlyBudget = $config.foundation.monthlyBudget; budgetIsSpendingCap = $false;
    limitations = @('Retail list estimate, not contracted price or actual invoice.', 'Shared resource charges and free grants are not independently attributable; no free allowance was subtracted.',
        'Usage quantities are operator assumptions; include both candidate and previous revisions, job executions, logs, egress, tokens and email.', 'Budget alerts are delayed and do not stop resource spending.') }
Save-TaskManagerPrivateJson $OutputPath $report
if ($missing.Count -or $total -gt $config.foundation.monthlyBudget) { throw 'Cost estimate is incomplete or exceeds the reviewed budget; deployment cost approval remains blocked.' }
Write-Host 'Current exact-meter estimate saved with timestamp, quantities and limitations. It is not a spending guarantee.'
