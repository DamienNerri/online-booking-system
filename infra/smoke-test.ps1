# Smoke test end-to-end via le répartiteur Nginx.
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:8088'

Write-Host "1) Inscription"
$email = "user$(Get-Random)@test.local"
$reg = Invoke-RestMethod -Uri "$base/api/auth/register" -Method Post -ContentType 'application/json' `
    -Body (@{ email = $email; password = 'password123' } | ConvertTo-Json)
$token = $reg.token
Write-Host "   token obtenu, role=$($reg.role)"

$headers = @{ Authorization = "Bearer $token" }

Write-Host "2) Disponibilite"
$avail = Invoke-RestMethod -Uri "$base/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05" -Method Get
Write-Host "   $($avail.Count) slots disponibles"
$slotId = $avail[0].slotId

Write-Host "3) Reservation (hold) du slot $slotId"
$booking = Invoke-RestMethod -Uri "$base/api/bookings" -Method Post -Headers $headers -ContentType 'application/json' `
    -Body (@{ slotIds = @($slotId) } | ConvertTo-Json)
Write-Host "   bookingId=$($booking.bookingId) status=$($booking.status) expiresAt=$($booking.expiresAt)"

Write-Host "4) Confirmation"
$confirmed = Invoke-RestMethod -Uri "$base/api/bookings/$($booking.bookingId)/confirm" -Method Post -Headers $headers
Write-Host "   status=$($confirmed.status)"

Write-Host "5) Le slot ne doit plus etre disponible"
$avail2 = Invoke-RestMethod -Uri "$base/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05" -Method Get
$stillThere = $avail2 | Where-Object { $_.slotId -eq $slotId }
if ($stillThere) { throw "ECHEC: le slot reserve est encore liste comme disponible" }
Write-Host "   OK: slot retire des disponibilites ($($avail2.Count) restants)"

Write-Host "6) Annulation"
Invoke-RestMethod -Uri "$base/api/bookings/$($booking.bookingId)" -Method Delete -Headers $headers | Out-Null
$avail3 = Invoke-RestMethod -Uri "$base/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05" -Method Get
$back = $avail3 | Where-Object { $_.slotId -eq $slotId }
if (-not $back) { throw "ECHEC: le slot annule n'est pas redevenu disponible" }
Write-Host "   OK: slot redevenu disponible apres annulation"

Write-Host "`nSMOKE TEST REUSSI"
