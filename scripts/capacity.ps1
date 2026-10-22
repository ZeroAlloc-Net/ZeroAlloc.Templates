# Capacity run for one published template binary, measured with oha.
# See docs/benchmarks/2026-10-22-template-capacity.md for how it is used.
#
# Needs: Docker, oha on PATH or -Oha, and a Postgres container named by -PgContainer
# listening on -PgPort, for example:
#   docker run -d --name pgbench -p 55432:5432 -e POSTGRES_PASSWORD=postgres postgres:17 -c max_connections=2000
#
# Each scenario gets a fresh database and a fresh SUT process. The SUT and oha are
# pinned to disjoint CPU sets with -SutMask and -LoadMask; pick masks for your machine.
#
# Scenarios:
#   hit    GET one order, always served from the output cache
#   db     GET random orders with the output cache off: the full read path
#   mixed  GET random orders with the cache on, while 100 orders/s are created
#   write  POST /orders
param(
  [Parameter(Mandatory)][string]$Exe,
  [Parameter(Mandatory)][string]$Label,
  [string]$Out = (Join-Path (Get-Location) 'capacity-results'),
  [string]$Oha = 'oha',
  [string]$PgContainer = 'pgbench',
  [int]$PgPort = 55432,
  [ValidateSet('clean','vs')][string]$Template = 'clean',
  [hashtable]$SutEnv = @{},
  [string]$Pool = 'Maximum Pool Size=500',
  [int]$Dur = 15,
  [string[]]$Scenarios = @('hit','db','mixed','write'),
  [long]$SutMask = 0xF0,
  [long]$LoadMask = 0xFF000
)
$ErrorActionPreference = 'Stop'
$oha = $Oha
$out = Join-Path $Out $Label; New-Item -ItemType Directory -Force $out | Out-Null

# HS256 token signed with the template's development key, holding every scope.
function B64Url([byte[]]$b) { [Convert]::ToBase64String($b).TrimEnd('=').Replace('+','-').Replace('/','_') }
$jwtHeader = B64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$jwtPayload = B64Url ([Text.Encoding]::UTF8.GetBytes((@{ sub = 'capacity'; exp = [DateTimeOffset]::UtcNow.AddDays(1).ToUnixTimeSeconds();
  scope = @('orders.read','orders.write','customers.read','customers.write') } | ConvertTo-Json -Compress)))
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes('DEV-ONLY-KEY-DO-NOT-USE-IN-PRODUCTION-AT-LEAST-32-CHARS-LONG'))
$token = "$jwtHeader.$jwtPayload." + (B64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$jwtHeader.$jwtPayload"))))
$auth = "Authorization: Bearer $token"
$base = 'http://127.0.0.1:5000'
$body = if ($Template -eq 'clean') { '{"customerId":1,"items":[{"sku":"SKU-1","quantity":1,"unitPriceEur":10}],"shippingZip":"1011AA"}' } else { '{"customerId":1,"total":10}' }
$randUrl = "$base/orders/([1-9][0-9]{0,2}|1000)"

function Reset-Db {
  docker exec $PgContainer psql -U postgres -q -c "DROP DATABASE IF EXISTS myapp_load WITH (FORCE);" | Out-Null
  docker exec $PgContainer psql -U postgres -q -c "CREATE DATABASE myapp_load;" | Out-Null
}

function Start-Sut([hashtable]$extra) {
  $env:ASPNETCORE_URLS = $base
  $env:ASPNETCORE_ENVIRONMENT = 'Production'
  $env:Database__Provider = 'Postgres'
  $env:Database__SchemaStrategy = 'EmbeddedScript'
  $env:ConnectionStrings__Default = "Host=127.0.0.1;Port=$PgPort;Username=postgres;Password=postgres;Database=myapp_load;$Pool"
  $env:Shipping__UseStub = 'true'
  $env:OutputCache__OrderByIdTtlSeconds = $null
  foreach ($k in $SutEnv.Keys) { Set-Item "env:$k" $SutEnv[$k] }
  foreach ($k in $extra.Keys) { Set-Item "env:$k" $extra[$k] }
  $p = Start-Process $Exe -WorkingDirectory (Split-Path $Exe) -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput "$out\sut.out.log" -RedirectStandardError "$out\sut.err.log"
  $p.ProcessorAffinity = [IntPtr]::new($SutMask)
  foreach ($i in 1..60) {
    try { if ((Invoke-WebRequest "$base/healthz" -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200) { return $p } } catch { Start-Sleep -Milliseconds 500 }
  }
  throw "SUT did not become healthy"
}

function Run-Oha([string]$name, [string[]]$ohaArgs) {
  $file = "$out\$name.json"
  $p = Start-Process $oha -ArgumentList (@('--no-tui','--output-format','json','-H',"`"$auth`"") + $ohaArgs) `
    -PassThru -WindowStyle Hidden -RedirectStandardOutput $file -RedirectStandardError "$out\$name.err"
  $p.ProcessorAffinity = [IntPtr]::new($LoadMask)
  return $p
}

function Summary([string]$name) {
  $j = Get-Content "$out\$name.json" -Raw | ConvertFrom-Json
  $codes = ($j.statusCodeDistribution.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' '
  [pscustomobject]@{ label=$Label; run=$name; rps=[math]::Round($j.summary.requestsPerSec);
    p50ms=[math]::Round($j.latencyPercentiles.p50*1000,2); p99ms=[math]::Round($j.latencyPercentiles.p99*1000,2);
    success=$j.summary.successRate; codes=$codes }
}

$results = @()
foreach ($sc in $Scenarios) {
  Reset-Db
  $extra = @{}
  if ($sc -eq 'db') { $extra['OutputCache__OrderByIdTtlSeconds'] = '0' }
  $sut = Start-Sut $extra
  try {
    # 1000 orders, ids 1..1000
    (Run-Oha "$sc-seed" @('-n','1000','-c','8','-m','POST','-H','"Content-Type: application/json"','-d',"`"$($body.Replace('"','\"'))`"",$("$base/orders"))).WaitForExit()
    $get = @('-c','256','--rand-regex-url',$randUrl)
    $post = @('-m','POST','-H','"Content-Type: application/json"','-d',"`"$($body.Replace('"','\"'))`"","$base/orders")
    switch ($sc) {
      'hit'   { $warm = @('-z','5s','-c','256',"$base/orders/1"); $meas = @('-z',"$($Dur)s",'-c','256',"$base/orders/1") }
      'db'    { $warm = @('-z','5s') + $get; $meas = @('-z',"$($Dur)s") + $get }
      'mixed' { $warm = @('-z','5s') + $get; $meas = @('-z',"$($Dur)s") + $get }
      'write' { $warm = @('-z','5s','-c','64') + $post; $meas = @('-z',"$($Dur)s",'-c','64') + $post }
    }
    (Run-Oha "$sc-warm" $warm).WaitForExit()
    $bg = $null
    if ($sc -eq 'mixed') { $bg = Run-Oha "$sc-writes" (@('-z',"$($Dur)s",'-c','4','-q','100') + $post) }
    (Run-Oha $sc $meas).WaitForExit()
    if ($bg) { $bg.WaitForExit(); $results += Summary "$sc-writes" }
    $results += Summary $sc
  } finally {
    Stop-Process -Id $sut.Id -Force
    Start-Sleep -Milliseconds 500
  }
}
$results | Format-Table -AutoSize | Out-String -Width 200
$results | ConvertTo-Json | Set-Content "$out\summary.json"
