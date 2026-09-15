<#
═══════════════════════════════════════════════════════════════════════
 simular-webhook.ps1 — hace de "pasarela de pagos" en DESARROLLO

 Cuando un cliente paga con tarjeta, la pasarela real avisa a la API con un
 webhook FIRMADO. En desarrollo usamos la pasarela SIMULADA, así que este
 script manda ese aviso: arma el JSON, lo firma con HMAC-SHA256 usando el
 secreto de User Secrets y lo envía a /api/webhooks/pasarela.

 Uso (con la API corriendo en http://localhost:3000):

     .\herramientas\simular-webhook.ps1 -Referencia sim_3f2a... -Monto 91.00
     .\herramientas\simular-webhook.ps1 -Referencia sim_3f2a... -Monto 91.00 -Tipo pago.rechazado

 La referencia y el monto salen de la respuesta de
 POST /api/pedidos/{id}/pagos/pasarela (y del total del pedido).

 Prueba de seguridad: agrega -SecretoFalso y la API responde 401.
═══════════════════════════════════════════════════════════════════════
#>
param(
    [Parameter(Mandatory = $true)][string]$Referencia,
    [Parameter(Mandatory = $true)][decimal]$Monto,
    [ValidateSet("pago.aprobado", "pago.rechazado")][string]$Tipo = "pago.aprobado",
    [string]$Api = "http://localhost:3000",
    [switch]$SecretoFalso
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

# El secreto se lee de User Secrets: nunca se escribe en un archivo del repositorio.
$linea = dotnet user-secrets list --project "src\RiverVet.Api\RiverVet.Api.csproj" |
    Where-Object { $_ -like "Pagos:Pasarela:SecretoWebhook = *" }
if (-not $linea) { throw "No hay Pagos:Pasarela:SecretoWebhook en User Secrets. Ejecuta configurar-desarrollo.ps1." }
$secreto = ($linea -split " = ", 2)[1]
if ($SecretoFalso) { $secreto = "un-atacante-no-conoce-el-secreto-real" }

$evento = [ordered]@{
    id         = "evt_" + [guid]::NewGuid().ToString("N")
    tipo       = $Tipo
    referencia = $Referencia
    monto      = $Monto
    moneda     = "PEN"
}
$json = $evento | ConvertTo-Json -Compress
# ConvertTo-Json respeta la cultura invariante para números decimales.
$cuerpo = [System.Text.Encoding]::UTF8.GetBytes($json)
$tiempo = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

# firma = hex(HMAC-SHA256(secreto, "{tiempo}.{cuerpo}")) — igual que FirmaWebhook.cs
$hmac = New-Object System.Security.Cryptography.HMACSHA256 (, [System.Text.Encoding]::UTF8.GetBytes($secreto))
$prefijo = [System.Text.Encoding]::UTF8.GetBytes("$tiempo.")
$mensaje = New-Object byte[] ($prefijo.Length + $cuerpo.Length)
[Array]::Copy($prefijo, 0, $mensaje, 0, $prefijo.Length)
[Array]::Copy($cuerpo, 0, $mensaje, $prefijo.Length, $cuerpo.Length)
$firma = -join ($hmac.ComputeHash($mensaje) | ForEach-Object { $_.ToString("x2") })

Write-Host "Enviando $Tipo para $Referencia por S/ $Monto ..." -ForegroundColor Cyan
try {
    $respuesta = Invoke-RestMethod -Method Post -Uri "$Api/api/webhooks/pasarela" -Body $cuerpo `
        -ContentType "application/json" -Headers @{ "X-Firma" = $firma; "X-Firma-Tiempo" = "$tiempo" }
    Write-Host ("Resultado: " + $respuesta.data.resultado) -ForegroundColor Green
}
catch {
    Write-Host ("La API rechazó el aviso: " + $_.Exception.Message) -ForegroundColor Yellow
}
