# Analyze counter CSVs side-by-side
param(
    [string]$Csv5k = "counters-vs-5k.csv",
    [string]$Csv8k = "counters-vs-8k.csv"
)

function Get-CounterStats {
    param([string]$Path, [string[]]$KeyContains)
    $rows = Import-Csv -Path $Path -Header @("Timestamp","Provider","CounterName","Type","Value")
    $rows = $rows | Where-Object { $_.Timestamp -ne "Timestamp" }
    $rows | ForEach-Object { $_ | Add-Member -NotePropertyName Ts -NotePropertyValue ([DateTime]::Parse($_.Timestamp)) -Force }
    $minTs = ($rows | Measure-Object Ts -Minimum).Minimum
    $sustain = $rows | Where-Object { ($_.Ts - $minTs).TotalSeconds -ge 12 }
    $result = @()
    foreach ($key in $KeyContains) {
        $filtered = $sustain | Where-Object { $_.CounterName -like "*$key*" }
        if (-not $filtered) { continue }
        $groups = $filtered | Group-Object CounterName
        foreach ($g in $groups) {
            $vals = $g.Group | ForEach-Object { [double]$_.Value }
            $stats = $vals | Measure-Object -Average -Maximum -Minimum
            # Simplify name by stripping verbose pool tags
            $short = $g.Name -replace "db.client.connection.pool.name=Host=127.0.0.1;Port=5432;Username=postgres;Database=myapp_load;Maximum Pool Size=500;?", "" `
                              -replace "db.system.name=postgresql;server.address=127.0.0.1;server.port=5432;?", "" `
                              -replace "network.protocol.version=1.1;url.scheme=http;?", "" `
                              -replace "http.route=/orders/\{id:int\}", "GET-orders-id" `
                              -replace "http.route=/orders", "POST-orders"
            $result += [pscustomobject]@{
                Counter = $short
                Mean    = [math]::Round($stats.Average, 4)
                Peak    = $stats.Maximum
                Samples = $vals.Count
            }
        }
    }
    return $result
}

$keys = @(
    "thread_pool.queue.length",
    "thread_pool.thread.count",
    "thread_pool.work_item.count",
    "gc.pause.time",
    "gc.heap.total_allocated",
    "gc.collections",
    "monitor.lock_contentions",
    "exceptions",
    "process.cpu.time",
    "process.memory.working_set",
    "db.client.connection.count",
    "db.client.connection.max",
    "db.client.connection.npgsql.pending_requests",
    "db.client.operation.npgsql.executing",
    "db.client.operation.duration",
    "http.server.active_requests",
    "http.server.request.duration"
)

$stats5k = Get-CounterStats -Path $Csv5k -KeyContains $keys
$stats8k = Get-CounterStats -Path $Csv8k -KeyContains $keys

# Merge into side-by-side table
$all = @{}
foreach ($s in $stats5k) { $all[$s.Counter] = @{ k5_mean = $s.Mean; k5_peak = $s.Peak; k8_mean = $null; k8_peak = $null } }
foreach ($s in $stats8k) {
    if (-not $all.ContainsKey($s.Counter)) { $all[$s.Counter] = @{ k5_mean = $null; k5_peak = $null } }
    $all[$s.Counter].k8_mean = $s.Mean
    $all[$s.Counter].k8_peak = $s.Peak
}
$rows = $all.Keys | Sort-Object | ForEach-Object {
    [pscustomobject]@{
        Counter      = $_
        Mean5k       = $all[$_].k5_mean
        Peak5k       = $all[$_].k5_peak
        Mean8k       = $all[$_].k8_mean
        Peak8k       = $all[$_].k8_peak
    }
}
$rows | ConvertTo-Json -Depth 4 | Out-File -Encoding utf8 -FilePath "counter-comparison.json"
$rows | Format-Table -AutoSize | Out-String -Width 220
