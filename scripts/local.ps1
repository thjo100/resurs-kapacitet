<#
.SYNOPSIS
  Kör Resurs & Kapacitet som container lokalt på Windows (Podman eller Docker).

.DESCRIPTION
  Samlar vardagskommandona i ett skript. Databasen ligger i en namngiven volym
  (resurs-data) som överlever att containern byts ut, så en uppgradering skriver
  aldrig över datan. Appen tar dessutom själv en kopia före varje schemaändring
  (volymen: /data/backups).

  .\scripts\local.ps1 <kommando> [argument]

  start            Bygg imagen om den saknas och starta på http://localhost:8080
  stop             Stoppa containern (datan ligger kvar)
  restart          Starta om
  status           Visa container, version och hälsa
  logs             Följ loggen (Ctrl+C avslutar)
  build            Bygg om imagen från nuvarande kod
  upgrade          Säkerhetskopiera -> git pull -> bygg -> byt ut containern (datan behålls)
  backup           Stoppa kort och spara hela datakatalogen som .tgz i .\backups
  restore <fil>    Återställ en .tgz från backup (ersätter nuvarande data!)
  reset            RADERA all data och börja om (kräver att du skriver JA)
  shell            Öppna ett skal i containern (felsökning)
  import <json> [apply]
                   Läs in verksamhetsdata från JSON i den körande appen. Utan "apply" är det en
                   torrkörning som bara visar vad som skulle hända. Uppdaterar/lägger till, raderar aldrig.
  load <json>      Börja om med tom databas som fylls från JSON (backup tas först). Se docs/egen-data.md.

  Miljövariabler (valfria):
    RK_ENGINE   podman eller docker (annars autodetekteras, podman först)
    RK_PORT     värdport, standard 8080
    RK_DEMO     true/false – skapa demodata när databasen är tom (standard true)
    ADMIN_PASSWORD  lösenord för admin när databasen skapas (standard: admin)
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Command = 'help',
    [Parameter(Position = 1)][string]$Arg,
    [Parameter(Position = 2)][string]$Arg2
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)   # repots rot

$Image     = 'resurs-kapacitet:local'
$Container = 'resurs-kapacitet'
$Volume    = 'resurs-data'
$Port      = if ($env:RK_PORT) { $env:RK_PORT } else { '8080' }
$Demo      = if ($env:RK_DEMO) { $env:RK_DEMO } else { 'true' }
$BackupDir = Join-Path (Get-Location) 'backups'
$SeedFile  = $null   # sätts av 'load'

# ---- Välj container-motor ----
$Engine = $env:RK_ENGINE
if (-not $Engine) {
    foreach ($e in 'podman', 'docker') {
        if (Get-Command $e -ErrorAction SilentlyContinue) { $Engine = $e; break }
    }
}
if (-not $Engine) { throw 'Hittar varken podman eller docker. Se docs/lokal-windows.md.' }

function Eng { & $Engine @args; if ($LASTEXITCODE -ne 0) { throw "$Engine $($args -join ' ') misslyckades ($LASTEXITCODE)" } }
function EngQuiet { & $Engine @args 2>$null }

function Test-Running { (EngQuiet ps --filter "name=^$Container$" --format '{{.Names}}') -eq $Container }
function Test-Exists  { (EngQuiet ps -a --filter "name=^$Container$" --format '{{.Names}}') -eq $Container }

function Get-AppVersion {
    # Senaste versionen = första posten i ReleaseNotes.json
    $rn = Get-Content 'backend/src/ResursKapacitet.Api/ReleaseNotes.json' -Raw -Encoding UTF8 | ConvertFrom-Json
    $rn[0].version
}

function Build-Image {
    $ver = Get-AppVersion
    $sha = (git rev-parse --short HEAD 2>$null)
    $a = @('build')
    if ($Engine -eq 'podman') { $a += @('--format', 'docker') }   # behövs för HEALTHCHECK
    $a += @('-f', 'Containerfile', '-t', $Image,
               '--build-arg', "APP_VERSION=$ver-lokal",
               '--build-arg', "GIT_SHA=$sha",
               '--build-arg', "BUILD_DATE=$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))",
               '.')
    Write-Host "==> Bygger $Image (version $ver) – första bygget tar några minuter" -ForegroundColor Cyan
    Eng @a
}

function Start-App {
    if (Test-Exists) { EngQuiet rm -f $Container | Out-Null }
    $run = @('run', '-d', '--name', $Container, '-p', "${Port}:8080",
             '-v', "${Volume}:/data", '-e', "SEED_DEMO=$Demo", '-e', 'TZ=Europe/Stockholm',
             '--restart', 'unless-stopped')
    if ($SeedFile) { $run += @('-e', "SEED_FILE=$SeedFile") }
    if ($env:ADMIN_PASSWORD) { $run += @('-e', "ADMIN_PASSWORD=$($env:ADMIN_PASSWORD)") }
    $run += $Image
    Eng @run | Out-Null
    Write-Host '==> Väntar på att appen ska bli klar' -ForegroundColor Cyan
    for ($i = 0; $i -lt 40; $i++) {
        try {
            $v = Invoke-RestMethod "http://localhost:$Port/api/version" -TimeoutSec 2
            Write-Host "==> Klart! Version $v på http://localhost:$Port" -ForegroundColor Green
            if ($Demo -eq 'true') { Write-Host '    Demokonton: admin/admin och planerare/planerare' }
            return
        } catch { Start-Sleep -Seconds 2 }
    }
    Write-Warning "Appen svarar inte ännu. Kolla: .\scripts\local.ps1 logs"
}

function Stop-App { if (Test-Running) { Write-Host '==> Stoppar containern'; Eng stop -t 20 $Container | Out-Null } }

function Backup-Data {
    New-Item -ItemType Directory -Force $BackupDir | Out-Null
    $wasRunning = Test-Running
    Stop-App   # stoppad databas = garanterat konsistent kopia
    $name = "resurs-data-$(Get-Date -Format 'yyyyMMdd-HHmmss').tgz"
    Eng run --rm --entrypoint tar -v "${Volume}:/data" -v "${BackupDir}:/backup" $Image czf "/backup/$name" -C /data .
    Write-Host "==> Backup sparad: backups\$name" -ForegroundColor Green
    if ($wasRunning) { Eng start $Container | Out-Null }
    return $name
}

function Require-Image {
    EngQuiet image inspect $Image | Out-Null
    if ($LASTEXITCODE -ne 0) { Build-Image }
}

switch ($Command.ToLower()) {
    'start' {
        Require-Image
        if (Test-Running) { Write-Host "Körs redan: http://localhost:$Port" } else { Start-App }
    }
    'stop'    { Stop-App }
    'restart' { Stop-App; Require-Image; Start-App }
    'status'  {
        Eng ps -a --filter "name=^$Container$"
        try { Write-Host "Version: $(Invoke-RestMethod "http://localhost:$Port/api/version" -TimeoutSec 3)" } catch { Write-Host 'Svarar inte.' }
    }
    'logs'    { Eng logs -f --tail 100 $Container }
    'build'   { Build-Image }
    'backup'  { Backup-Data | Out-Null }
    'upgrade' {
        if (Test-Exists) { Backup-Data | Out-Null }
        Write-Host '==> Hämtar senaste koden' -ForegroundColor Cyan
        git pull --ff-only
        if ($LASTEXITCODE -ne 0) { throw 'git pull misslyckades – lös det först (lokala ändringar? commit/stash).' }
        Build-Image
        Stop-App
        Start-App   # nya containern använder samma volym; appen migrerar databasen själv
    }
    'restore' {
        if (-not $Arg -or -not (Test-Path $Arg)) { throw 'Ange en backupfil: .\scripts\local.ps1 restore backups\resurs-data-XXXX.tgz' }
        $file = Get-Item $Arg
        if ($file.DirectoryName -ne $BackupDir) { Copy-Item $file.FullName $BackupDir -Force }
        if ((Read-Host "Detta ERSÄTTER nuvarande data med $($file.Name). Skriv JA för att fortsätta") -ne 'JA') { return }
        Require-Image
        Stop-App
        Write-Host '==> Tar en säkerhetskopia av nuläget först'
        Backup-Data | Out-Null
        Eng run --rm --entrypoint sh -v "${Volume}:/data" -v "${BackupDir}:/backup" $Image `
            -c "find /data -mindepth 1 -delete && tar xzf /backup/$($file.Name) -C /data"
        Start-App
    }
    'reset' {
        if ((Read-Host 'Detta RADERAR all data permanent. Skriv JA för att fortsätta') -ne 'JA') { return }
        if (Test-Exists) { EngQuiet rm -f $Container | Out-Null }
        Eng volume rm -f $Volume | Out-Null
        Write-Host 'Data raderad. Kör "start" för en ny, tom databas.'
    }
    'import'  {
        if (-not $Arg -or -not (Test-Path $Arg)) { throw 'Ange en JSON-fil: .\scripts\local.ps1 import exempel\verksamhet.json [apply]' }
        $apply = $Arg2 -eq 'apply'
        $user = Read-Host 'Admin-användare (Enter = admin)'; if (-not $user) { $user = 'admin' }
        $pw = Read-Host 'Lösenord' -AsSecureString
        $plain = [Net.NetworkCredential]::new('', $pw).Password
        $base = "http://localhost:$Port"
        $login = @{ userName = $user; password = $plain } | ConvertTo-Json
        Invoke-RestMethod "$base/api/auth/login" -Method Post -ContentType 'application/json' -Body $login -SessionVariable sess | Out-Null
        $bytes = [IO.File]::ReadAllBytes((Resolve-Path $Arg))
        try {
            $r = Invoke-RestMethod "$base/api/import/json?dryRun=$((-not $apply).ToString().ToLower())" -Method Post `
                -ContentType 'application/json; charset=utf-8' -Body $bytes -WebSession $sess
        } catch { throw "Importen avvisades: $($_.ErrorDetails.Message)" }
        Write-Host ("Nya: {0} team, {1} resurser, {2} objekt, {3} uppdrag, {4} fördelningar ({5} h i filen)" -f `
            $r.newTeams, $r.newResources, $r.newObjects, $r.newAssignments, $r.newAllocations, $r.hours)
        if ($r.errors.Count) { Write-Host 'FEL (inget sparades):' -ForegroundColor Red; $r.errors | ForEach-Object { Write-Host "  - $_" } }
        elseif ($r.applied) { Write-Host 'Importerat!' -ForegroundColor Green }
        else { Write-Host 'Torrkörning – inget sparades. Kör igen med "apply" sist för att spara.' -ForegroundColor Yellow }
    }
    'load'    {
        if (-not $Arg -or -not (Test-Path $Arg)) { throw 'Ange en JSON-fil: .\scripts\local.ps1 load exempel\verksamhet.json' }
        if ((Read-Host 'Detta ERSÄTTER hela databasen med innehållet i filen (backup tas först). Skriv JA') -ne 'JA') { return }
        $file = Get-Item $Arg
        Require-Image
        EngQuiet volume inspect $Volume | Out-Null
        if ($LASTEXITCODE -eq 0) { Backup-Data | Out-Null }
        if (Test-Exists) { EngQuiet rm -f $Container | Out-Null }
        EngQuiet volume rm -f $Volume | Out-Null
        Eng run --rm --entrypoint cp -v "${Volume}:/data" -v "$($file.DirectoryName):/in" $Image "/in/$($file.Name)" /data/seed.json
        $SeedFile = '/data/seed.json'
        Start-App
        Write-Host 'Kontrollera loggen vid fel: .\scripts\local.ps1 logs' 
    }
    'shell'   { Eng exec -it $Container sh }
    default   { Get-Help $PSCommandPath -Detailed }
}
