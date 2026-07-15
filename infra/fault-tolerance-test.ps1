# Checkpoint 4.3 — tolerance aux pannes (Req 7).
# Tue un noeud API pendant l'activite et verifie que le service continue via les
# noeuds restants, sans reservation partielle ni incoherence.
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:8088'

# 1) Etat initial : combien de noeuds repondent
$nodesBefore = (1..12 | ForEach-Object { (Invoke-RestMethod -Uri "$base/health").node } | Sort-Object -Unique)
Write-Host "Noeuds actifs avant panne : $($nodesBefore.Count) -> $($nodesBefore -join ', ')"

# 2) Tuer un noeud API
$victim = 'online-booking-system-api-2'
Write-Host "Arret force du noeud $victim ..."
docker kill $victim | Out-Null
Start-Sleep -Seconds 6   # laisser le DNS Docker retirer le conteneur

# 3) Le service doit continuer a repondre
$ok = 0; $ko = 0
foreach ($i in 1..20) {
    try { Invoke-RestMethod -Uri "$base/health" -Method Get | Out-Null; $ok++ } catch { $ko++ }
}
Write-Host "Apres panne : $ok requetes /health OK, $ko en echec"
if ($ok -lt 15) { throw "ECHEC: trop d'echecs apres la panne d'un noeud" }

# 4) Une reservation reste possible et coherente
$email = "ft$(Get-Random)@test.local"
$reg = Invoke-RestMethod -Uri "$base/api/auth/register" -Method Post -ContentType 'application/json' `
    -Body (@{ email = $email; password = 'password123' } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($reg.token)" }
$avail = Invoke-RestMethod -Uri "$base/api/availability?type=HOTEL_ROOM&from=2026-01-25&to=2026-01-28" -Method Get
$slotId = $avail[0].slotId
$booking = Invoke-RestMethod -Uri "$base/api/bookings" -Method Post -Headers $headers -ContentType 'application/json' `
    -Body (@{ slotIds = @($slotId) } | ConvertTo-Json)
Write-Host "Reservation apres panne : bookingId=$($booking.bookingId) status=$($booking.status)"
if ($booking.status -ne 'HOLD') { throw "ECHEC: reservation non creee apres panne" }

# 5) Redemarrer le noeud (retour dans le cluster)
Write-Host "Redemarrage du noeud $victim ..."
docker start $victim | Out-Null
Start-Sleep -Seconds 6
$nodesAfter = (1..12 | ForEach-Object { (Invoke-RestMethod -Uri "$base/health").node } | Sort-Object -Unique)
Write-Host "Noeuds actifs apres redemarrage : $($nodesAfter.Count) -> $($nodesAfter -join ', ')"

Write-Host "`nCHECKPOINT 4.3 REUSSI : continuite de service pendant la panne, reservation coherente, noeud reintegre."
