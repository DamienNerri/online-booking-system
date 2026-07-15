# Tache 5.4 — tests de securite (Req 8, 9). Documente les anomalies attendues.
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:8088'
Add-Type -AssemblyName System.Net.Http

function Get-Status([scriptblock]$call) {
    try { & $call | Out-Null; return 200 }
    catch { return [int]$_.Exception.Response.StatusCode.value__ }
}

$fail = 0

Write-Host "S1) Injection SQL sur le parametre 'type'"
$payload = "HOTEL_ROOM'; DROP TABLE bookings;--"
$enc = [uri]::EscapeDataString($payload)
try {
    $r = Invoke-RestMethod -Uri "$base/api/availability?type=$enc&from=2026-01-01&to=2026-01-05" -Method Get
    # Requetes parametrees => aucune injection, resultat vide, tables intactes.
    $check = Invoke-RestMethod -Uri "$base/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05" -Method Get
    if ($check.Count -gt 0) { Write-Host "   OK: injection neutralisee, tables intactes ($($check.Count) slots)" }
    else { Write-Host "   ATTENTION: verifier l'etat des donnees"; $fail++ }
} catch { Write-Host "   OK: requete rejetee proprement ($($_.Exception.Message))" }

Write-Host "S2) Acces sans jeton (POST /api/bookings) -> 401 attendu"
$s = Get-Status { Invoke-RestMethod -Uri "$base/api/bookings" -Method Post -ContentType 'application/json' -Body '{"slotIds":[1]}' }
if ($s -eq 401) { Write-Host "   OK: 401" } else { Write-Host "   ECHEC: $s"; $fail++ }

Write-Host "S3) Jeton invalide -> 401 attendu"
$h = @{ Authorization = "Bearer not.a.real.token" }
$s = Get-Status { Invoke-RestMethod -Uri "$base/api/bookings" -Method Post -Headers $h -ContentType 'application/json' -Body '{"slotIds":[1]}' }
if ($s -eq 401) { Write-Host "   OK: 401" } else { Write-Host "   ECHEC: $s"; $fail++ }

Write-Host "S4) Controle d'acces (ownership) -> 403 attendu"
$a = Invoke-RestMethod -Uri "$base/api/auth/register" -Method Post -ContentType 'application/json' -Body (@{ email="a$(Get-Random)@t.local"; password="password123" } | ConvertTo-Json)
$b = Invoke-RestMethod -Uri "$base/api/auth/register" -Method Post -ContentType 'application/json' -Body (@{ email="b$(Get-Random)@t.local"; password="password123" } | ConvertTo-Json)
$avail = Invoke-RestMethod -Uri "$base/api/availability?type=HOTEL_ROOM&from=2026-01-05&to=2026-01-08" -Method Get
$slotId = $avail[0].slotId
$bk = Invoke-RestMethod -Uri "$base/api/bookings" -Method Post -Headers @{ Authorization="Bearer $($a.token)" } -ContentType 'application/json' -Body (@{ slotIds=@($slotId) } | ConvertTo-Json)
$s = Get-Status { Invoke-RestMethod -Uri "$base/api/bookings/$($bk.bookingId)" -Method Delete -Headers @{ Authorization="Bearer $($b.token)" } }
if ($s -eq 403) { Write-Host "   OK: 403 (B ne peut pas annuler la reservation de A)" } else { Write-Host "   ECHEC: $s"; $fail++ }

Write-Host "S5) Rate limiting -> 429 attendu sous rafale"
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $a.token)
$tasks = @()
foreach ($i in 1..500) { $tasks += $client.GetAsync("$base/health") }
[System.Threading.Tasks.Task]::WaitAll($tasks)
$codes = $tasks | ForEach-Object { [int]$_.Result.StatusCode }
$limited = ($codes | Where-Object { $_ -eq 429 }).Count
Write-Host "   Reponses 429 : $limited / 500"
if ($limited -gt 0) { Write-Host "   OK: la limitation de debit se declenche" } else { Write-Host "   ECHEC: aucune 429"; $fail++ }

if ($fail -gt 0) { throw "$fail test(s) de securite en echec" }
Write-Host "`nTESTS DE SECURITE REUSSIS"
