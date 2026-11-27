# Resurs & Kapacitet

Resurs- och kapacitetsplanering för systemutvecklare i team. Verktyget ersätter Excel-arket
*Resurs | Tillhörighet | Uppdrag | Förvaltningsobjekt/Projekt | JAN–DEC*. Det har samma snabba Excel-känsla, men också
beläggning, backlogg med drag-n-drop, sammanställningar, ändringslogg och admin.

- **Backend:** .NET 10 (ASP.NET Core minimal API), EF Core och SQLite.
- **Frontend:** Vue 3 och Vite, i JavaScript.
- **Leverans:** en container på Docker Hub som körs med Podman.
- **CI/CD:** GitHub Actions med PR-bygge och en egen PR-miljö per pull request.

## Funktioner

- **Planering som i Excel**
  - Expanderbara resurser med uppdragsrader och timmar per månad.
  - Tab, Enter och piltangenter för navigering, och du kan klistra in block direkt från Excel.
  - Ctrl+Enter fyller höger och Ctrl+Z ångrar.
  - Ändringar sparas automatiskt.
- **Kvar att fördela**
  - Fördelningspanelen visar estimat, fördelat och kvar för vald rad.
  - Den visar också resursens lediga tid i månaden.
- **Beläggning** per resurs, team och månad, med färgnivåer (ledigt, bra, överbelagd, kraftigt överbelagd).
  - Visningen kan växlas mellan timmar, procent och ledig tid.
- **Backlogg**
  - Prioritera genom att dra korten.
  - Dra ett uppdrag till en resurs för att tilldela det. Dialogen fördelar timmarna efter ledig tid, jämnt eller efter kapacitet.
- **Översikt**
  - Nyckeltal och diagram över kapacitet mot planerat.
  - Heatmap per team som kan expanderas till resurser.
  - Varningar, de högst prioriterade uppdragen i backloggen och de senaste ändringarna.
- **Uppdragssammanställning** per förvaltningsobjekt/projekt, med estimat, fördelat, kvar och timmar per månad.
- **Ändringslogg** som visar vem som ändrade vad och när. Den är sökbar, och ändringar från andra syns direkt.
- **Frånvaro och semester**
  - Sätts per resurs och månad.
  - Månadsfaktorer (t.ex. juli 25 %) och ett planeringstak gäller för alla.
- **Admin** för resurser, team, objekt, uppdrag, användare och inställningar.
  - Import från Excel.
  - Export till Excel.
  - Säkerhetskopia av databasen.
  - Kopiera ett års plan till ett annat år.
- **Nyheter**
  - Visar release notes i appen.
  - Efter en uppgradering visas "Nytt i version X".
  - En banner visar när en ny version har installerats.
- **Mobil och tema:** responsiv för mobil och surfplatta, med ljust och mörkt tema och mjuka expand/collapse-animationer.

Mer i [docs/funktionsspec.md](docs/funktionsspec.md) och [docs/designspec.md](docs/designspec.md).

## Roller och inloggning

Alla på intranätet kan **se** planeringen utan att logga in. Det kan stängas av under *Admin → Inställningar*.

| Roll | Kan |
|---|---|
| Läsare | se allt (när inloggning krävs för läsning) |
| Planerare | fördela tid, hantera backlogg och uppdrag, ange frånvaro |
| Admin | allt ovan, plus resurser, team, objekt, användare, inställningar och import |

Inloggningen är medvetet enkel (en användartabell) eftersom appen ligger på intranätet. Säkerheten ser ut så här:

- Lösenorden lagras ändå hashade med PBKDF2.
- Inloggningen sparas i en cookie med `HttpOnly` och `SameSite=Strict`.
- Inloggningsförsöken är begränsade till 10 per minut och IP-adress.

Första gången skapas kontot `admin`:

- Med variabeln `ADMIN_PASSWORD` satt får kontot det lösenordet.
- Annars får det lösenordet `admin`, som måste bytas vid första inloggningen.

## Kom igång lokalt

Krav: .NET 10 SDK och Node 22.

```bash
cd backend/src/ResursKapacitet.Api
```

```bash
SEED_DEMO=true ASPNETCORE_URLS=http://localhost:5080 dotnet run
```

Starta frontenden i en annan terminal:

```bash
cd frontend && npm install && npm run dev
```

Öppna http://localhost:5173. Med demodata finns kontona `admin`/`admin` och `planerare`/`planerare`.

Tester:

```bash
dotnet test backend/ResursKapacitet.slnx
```

```bash
cd frontend && npm test
```

### Demoläge (utan server)

```bash
cd frontend && npm run build:demo
```

Bygget ger `frontend/dist-demo/resurs-kapacitet-demo.html`. Det är en fristående fil med hela gränssnittet och en demo-backend i webbläsaren, och datan sparas i webbläsarens localStorage. Filen är bra för att visa upp eller testa gränssnittet. Export och säkerhetskopia fungerar bara i den riktiga installationen. Det vanliga bygget och containern innehåller ingen demokod.

### Databasändringar

Ändra entiteterna i `backend/src/ResursKapacitet.Api/Data` och skapa en migrering:

```bash
dotnet tool install --global dotnet-ef
```

```bash
cd backend/src/ResursKapacitet.Api && dotnet ef migrations add BeskrivandeNamn -o Data/Migrations
```

Migreringarna körs automatiskt när appen startar. Innan schemat ändras sparas en kopia av databasen i
`/data/backups`, och de 10 senaste kopiorna behålls. CI fallerar om en modelländring saknar migrering.

## Köra med Podman

```bash
podman build --format docker -t resurs-kapacitet .
```

```bash
podman run -d --name resurs-kapacitet -p 8080:8080 -v resurs-data:/data resurs-kapacitet
```

`--format docker` behövs för att `HEALTHCHECK` ska följa med i imagen.

### Miljövariabler

| Variabel | Standard | Beskrivning |
|---|---|---|
| `DATA_DIR` | `/data` | Plats för SQLite-databas, nycklar och backuper. Montera som volym. |
| `ADMIN_PASSWORD` | – | Lösenord för det första admin-kontot. |
| `SEED_DEMO` | `false` | Skapa demodata om databasen är tom. |
| `APP_ENV_LABEL` | – | Etikett i topbaren, t.ex. `PR #12` eller `Test`. |
| `TZ` | `Europe/Stockholm` | Tidszon. |
| `ConnectionStrings__Default` | – | Annan SQLite-sökväg vid behov. |

### Produktion med systemd (Quadlet) och automatiska uppgraderingar

```bash
mkdir -p ~/.config/containers/systemd
```

```bash
cp deploy/resurs-kapacitet.container ~/.config/containers/systemd/
```

Ändra raden `Image=` i filen till ert Docker Hub-konto. Starta sedan tjänsten:

```bash
systemctl --user daemon-reload && systemctl --user start resurs-kapacitet
```

```bash
loginctl enable-linger $USER
```

```bash
systemctl --user enable --now podman-auto-update.timer
```

- **Automatisk uppgradering:** `AutoUpdate=registry` gör att `podman auto-update` hämtar nya versioner av `:latest`. Om den nya versionen inte startar rullas den tillbaka automatiskt.
- **Manuell uppgradering eller tillbakarullning:** `deploy/upgrade.sh [version]`.
- **Lokal körning:** `deploy/compose.yaml` fungerar med `podman-compose`.

## CI/CD och PR-miljöer

Flödet: **PR → bygge och test → image `:pr-<nr>` → PR-miljö med egen adress → granska → merge → release och driftsättning.**

| Händelse | Vad händer |
|---|---|
| Pull request | Bygge och test (frontend och backend) och kontroll av migreringar. Imagen byggs med Podman, röktestas och pushas som `:pr-<nr>`. PR-miljön startas på `http://<PREVIEW_HOST>:<9000+nr>` med demodata, och en kommentar i PR:en får adressen. |
| Ny push i PR:en | PR-miljön uppdateras. Data i miljön ligger kvar mellan pushar. |
| PR stängs eller mergas | PR-miljön tas bort, och taggarna `pr-<nr>` raderas från Docker Hub. |
| Merge till main | Imagen pushas som `:<version>`, `:latest`, `:main` och `:sha-xxxxxxx`. En tagg `v<version>` och en GitHub Release skapas från release notes. Produktionen driftsätts om det är aktiverat. |

### Engångskonfiguration i GitHub

1. **Docker Hub.** Skapa ett repository och en *access token* (Read & Write; Delete om PR-taggarna ska städas bort). Lägg sedan in följande under *Settings → Secrets and variables → Actions*:
   - Secrets: `DOCKERHUB_USERNAME` och `DOCKERHUB_TOKEN`.
   - Variabel: `DOCKERHUB_IMAGE`, t.ex. `mittkonto/resurs-kapacitet`.
2. **Self-hosted runner på intranätet.** Den behövs för PR-miljöer och produktion.
   - Installera en GitHub Actions-runner på en Linux-server med Podman. Se *Settings → Actions → Runners → New self-hosted runner*.
   - Ge runnern etiketten `podman`.
   - Kör den som samma användare som kör Quadlet-tjänsten.
   - Variabler:
     - `PREVIEW_HOST`: serverns namn, t.ex. `resurs-test.intranet.local`. Variabeln aktiverar PR-miljöerna.
     - `PREVIEW_BASE_PORT`: valfri, standard `9000`.
     - `PREVIEW_URL_TEMPLATE`: valfri, t.ex. `https://pr-{pr}.resurs.intranet.local` om ni har en reverse proxy med wildcard-DNS.
   - PR-miljöer startas bara för PR:er från samma repository, aldrig från forkar.
3. **Produktion.**
   - Sätt `PRODUCTION_DEPLOY=true` och `PRODUCTION_URL` (t.ex. `http://resurs.intranet.local:8080`). `PRODUCTION_SERVICE` är valfri och har standardvärdet `resurs-kapacitet`.
   - Skapa environmentet `production` under *Settings → Environments* och lägg till *Required reviewers*. Då kräver varje driftsättning ett godkännande.
4. **Skydda main.** Under *Settings → Branches* (eller Rulesets) gör ni följande:
   - Kräv pull request.
   - Kräv att statuskontrollerna **Bygg & test** och **Container-image** är gröna.
   - Då testas allt i PR-miljön innan det når produktionen.

Utan Docker Hub-konfiguration byggs och röktestas imagen ändå i varje PR. Den publiceras bara inte.

### Släppa en ny version med "Nyheter"

Lägg till en ny post **överst** i `backend/src/ResursKapacitet.Api/ReleaseNotes.json`:

```json
{
  "version": "1.1.0",
  "date": "2026-12-15",
  "title": "Kortare rubrik",
  "items": [
    { "type": "new", "text": "Något nytt" },
    { "type": "improved", "text": "Något bättre" },
    { "type": "fix", "text": "Något rättat" }
  ]
}
```

Versionsnumret blir image-taggen och GitHub-releasen. Efter uppgraderingen händer följande för användarna:

- Den som har appen öppen ser en banner om att det finns en ny version.
- Vid nästa besök visas dialogen "Nytt i version 1.1.0".
- Menyn *Nyheter* får en prick.

CI varnar i PR:en om versionen inte har höjts.

## Projektstruktur

```
backend/
  src/ResursKapacitet.Api/      .NET 10 API (Program.cs, Endpoints/, Data/, Services/, ReleaseNotes.json)
  tests/ResursKapacitet.Tests/  xUnit – API-, behörighets- och beräkningstester
frontend/                        Vue 3 + Vite (src/views, src/components, src/styles, src/utils)
deploy/                          Podman Quadlet, compose, upgrade.sh
docs/                            Funktions- och designspecifikation
.github/workflows/               CI/CD (ci.yml, pr-cleanup.yml)
.claude/agents/                  Expertagenter: resursplanerare och ux-designer
Containerfile                    Multi-stage build (Node → .NET SDK → ASP.NET runtime)
```
