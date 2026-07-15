# Checkpoint 3.2 — preuve d'absence de surreservation (Property 2, Req 3).
# Lance N requetes VRAIMENT paralleles sur le meme slot et verifie qu'exactement
# une seule reussit (201), toutes les autres etant rejetees en conflit (409).
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:8088'
$N = 30

Add-Type -AssemblyName System.Net.Http

# 1) Utilisateur + token
$email = "conc$(Get-Random)@test.local"
$reg = Invoke-RestMethod -Uri "$base/api/auth/register" -Method Post -ContentType 'application/json' `
    -Body (@{ email = $email; password = 'password123' } | ConvertTo-Json)
$token = $reg.token

# 2) Choisir un slot disponible
$avail = Invoke-RestMethod -Uri "$base/api/availability?type=HOTEL_ROOM&from=2026-01-15&to=2026-01-20" -Method Get
$slotId = $avail[0].slotId
Write-Host "Slot cible = $slotId ; lancement de $N requetes concurrentes..."

# 3) Tirer N requetes concurrentes avec HttpClient
$client = [System.Net.Http.HttpClient]::new()
$client.DefaultRequestHeaders.Authorization =
    [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
$json = (@{ slotIds = @($slotId) } | ConvertTo-Json)

$tasks = @()
foreach ($i in 1..$N) {
    $content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, 'application/json')
    $tasks += $client.PostAsync("$base/api/bookings", $content)
}
[System.Threading.Tasks.Task]::WaitAll($tasks)

# 4) Compter les codes de reponse
$codes = $tasks | ForEach-Object { [int]$_.Result.StatusCode }
$success = ($codes | Where-Object { $_ -eq 201 }).Count
$conflict = ($codes | Where-Object { $_ -eq 409 }).Count
$other = ($codes | Where-Object { $_ -ne 201 -and $_ -ne 409 }).Count

Write-Host "Resultats : succes(201)=$success  conflit(409)=$conflict  autre=$other"

if ($success -ne 1) { throw "ECHEC: $success reservations ont reussi (attendu: exactement 1) => SURRESERVATION" }
if ($other -ne 0)   { throw "ECHEC: $other reponses inattendues" }
if ($conflict -ne ($N - 1)) { throw "ECHEC: nombre de conflits inattendu" }

Write-Host "`nCHECKPOINT 3.2 REUSSI : exactement 1 reservation, $conflict conflits. Invariant anti-surreservation respecte."
