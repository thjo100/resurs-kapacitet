# CLAUDE.md

Resurs & Kapacitet: resurs- och kapacitetsplanering. Backend .NET 10 (minimal API, EF Core SQLite), frontend Vue 3 + Vite (JavaScript).
UI-text och kodkommentarer skrivs på svenska.

## Kommandon

- Backend-tester: `dotnet test backend/ResursKapacitet.slnx`
- Kör API med demodata: `cd backend/src/ResursKapacitet.Api && SEED_DEMO=true ASPNETCORE_URLS=http://localhost:5080 dotnet run`
- Frontend: `cd frontend && npm install && npm run dev` (proxar `/api` till :5080), tester `npm test`, bygg `npm run build`
- Ny migrering: `cd backend/src/ResursKapacitet.Api && dotnet ef migrations add <Namn> -o Data/Migrations`
- Container: `podman build --format docker -t resurs-kapacitet .`

## Konventioner

- Alla ändringar som ska synas för användare: ny post överst i `backend/src/ResursKapacitet.Api/ReleaseNotes.json` (höj versionen).
- Varje skrivande endpoint loggar en läsbar rad i ändringsloggen via `db.Log(...)`.
- Roller: policy `Planner` (Planerare + Admin) och `Admin`. Läs-API:er är öppna om inte inställningen "Kräv inloggning" är på.
- Timmar är heltal. Kapacitet räknas i `Services/CapacityCalculator.cs`; motsvarande nivålogik i `frontend/src/utils/calc.js`.
- Designtokens i `frontend/src/styles/tokens.css` – använd variabler, inga hårdkodade färger. Mjuka övergångar via `components/Expander.vue`.
- Experter finns som agenter i `.claude/agents/` (resursplanerare, ux-designer).
