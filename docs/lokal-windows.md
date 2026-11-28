# Köra Resurs & Kapacitet lokalt på Windows (container)

Mål: bygga imagen från koden och köra den på **http://localhost:8080**, med en databas som överlever uppgraderingar.
Allt nedan körs i **PowerShell**.

## 1. Installera verktyg (en gång)

För att bara köra containern behövs **Git** och en **container-motor**. .NET SDK och Node behövs bara om du vill
utveckla utanför containern (avsnitt 6).

```powershell
winget install --id Git.Git -e
```

Containermotorn kräver WSL2 (Linux-lager i Windows). Kör som administratör och starta om datorn om du blir ombedd:

```powershell
wsl --install --no-distribution
```

Välj **en** motor. Repot och produktionen använder Podman, så den är det rekommenderade valet (gratis).
Docker Desktop fungerar också men kräver betald licens i större företag.

```powershell
winget install --id RedHat.Podman -e
```

Öppna ett nytt PowerShell-fönster och skapa Podmans lilla Linux-VM:

```powershell
podman machine init
```

```powershell
podman machine start
```

(Docker Desktop istället: `winget install --id Docker.DockerDesktop -e`, starta det en gång och godkänn villkoren.)

Valfritt, för utveckling utanför container:

```powershell
winget install --id Microsoft.DotNet.SDK.10 -e
```

```powershell
winget install --id OpenJS.NodeJS.LTS -e
```

## 2. Hämta koden och tillåt skriptet

```powershell
git clone https://github.com/thjo100/resurs-kapacitet.git
```

```powershell
cd resurs-kapacitet
```

Tillåt lokala skript för din användare (en gång):

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## 3. Starta

```powershell
.\scripts\local.ps1 start
```

Första bygget tar några minuter (hämtar Node- och .NET-images). Sedan öppnar du http://localhost:8080 och loggar in
med `admin`/`admin` (demodata ingår). För tom databas utan demodata, och eget adminlösenord:

```powershell
$env:RK_DEMO = 'false'; $env:ADMIN_PASSWORD = 'byt-mig'; .\scripts\local.ps1 start
```

Skriptet autodetekterar Podman eller Docker. Byt port med `$env:RK_PORT = '9090'`.

## 4. Vardagskommandon

| Kommando | Gör |
|---|---|
| `.\scripts\local.ps1 stop` / `start` / `restart` | Stoppa/starta. Datan ligger kvar. |
| `.\scripts\local.ps1 status` | Container, version, svarar den? |
| `.\scripts\local.ps1 logs` | Följ loggen. |
| `.\scripts\local.ps1 build` | Bygg om imagen efter kodändring (sen `restart`). |
| `.\scripts\local.ps1 backup` | Spara hela datakatalogen som `backups\resurs-data-<tid>.tgz`. |
| `.\scripts\local.ps1 restore backups\<fil>.tgz` | Återställ en backup (tar en ny backup av nuläget först). |
| `.\scripts\local.ps1 upgrade` | Backup, `git pull`, ombygge och byt ut containern. |
| `.\scripts\local.ps1 reset` | Radera all data och börja om (frågar först). |

## 5. Uppgraderingar och databasen – hur det är tänkt att fungera

- Databasen (SQLite), inloggningsnycklar och backuper ligger i volymen **`resurs-data`** (monterad på `/data`).
  Volymen är skild från containern/imagen. När du bygger om och byter container **skrivs databasen inte över**.
- Dina data försvinner bara om du kör `reset`, tar bort volymen manuellt (`podman volume rm resurs-data`) eller
  anger `-v` vid `rm`. Undvik `podman system prune --volumes`.
- Vid start jämför appen databasens schema med koden och kör nya **migreringar** automatiskt. **Före** varje
  schemaändring tas en konsistent kopia i volymen (`/data/backups`, de 10 senaste sparas).
- `upgrade` tar dessutom en egen backup till `backups\` på din dator innan något ändras. Det är din skyddsväst
  eftersom backuperna i volymen försvinner om volymen gör det.
- Rulla tillbaka: `git checkout <tidigare-tagg>`, `.\scripts\local.ps1 build`, och vid behov
  `restore` av backupen från före uppgraderingen. En ny version som har ändrat schemat kan inte alltid köras mot
  databas som redan migrerats framåt, så återställ då backupen också.
- Demodata skapas bara när databasen är **tom**. Befintlig data rörs aldrig av `SEED_DEMO`.
- Appens egen backup: *Admin → Säkerhetskopia* (laddar ner en `.db`-fil).
- Vill du kunna bläddra i filerna på disk: använd `backup` och packa upp `.tgz` (7-Zip eller `tar -xzf`). Montera
  inte en Windows-mapp direkt som `/data`: SQLite och filrättigheter beter sig illa över Windows-filsystemet.

## 6. Utveckla utan container (snabbare loop)

```powershell
cd backend\src\ResursKapacitet.Api
$env:SEED_DEMO='true'; $env:ASPNETCORE_URLS='http://localhost:5080'; dotnet run
```

I ett andra fönster:

```powershell
cd frontend; npm install; npm run dev
```

Öppna http://localhost:5173. Den här körningen använder en egen databas i `backend\src\ResursKapacitet.Api\data`
och är helt skild från containerns.

## Felsökning

- **`podman`/`docker` hittas inte:** öppna ett nytt PowerShell-fönster efter installationen.
- **Podman: "Cannot connect"**: kör `podman machine start`.
- **Port 8080 upptagen:** `$env:RK_PORT='9090'` och starta om.
- **Skriptet får inte köras:** se `Set-ExecutionPolicy` ovan.
- **Appen svarar inte:** `.\scripts\local.ps1 logs`.
