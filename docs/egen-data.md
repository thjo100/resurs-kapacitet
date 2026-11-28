# Egen verksamhetsdata via JSON

Du kan bygga upp riktig data i en JSON-fil och läsa in den i appen. Utgå från mallen
[`exempel/verksamhet.json`](../exempel/verksamhet.json) (kopiera den, t.ex. till `data-lokal/verksamhet.json`, och fyll på).
Kommentarer (`//`) och avslutande komma är tillåtna.

## Regler

- Alla delar är valfria: `settings`, `teams`, `resources`, `objects`, `assignments`, `allocations`.
- Poster matchas på **namn** (skiftlägesokänsligt). Finns de uppdateras de med de fält du anger, annars skapas de. **Inget raderas.**
  Det betyder att samma fil kan köras flera gånger och att du kan läsa in en liten fil med bara ändringar.
- `allocations.hours` är exakt 12 heltal (JAN–DEC) och **ersätter** timmarna för den raden och det året.
  Resursen och uppdraget måste finnas (i filen eller redan i appen). Skriv `object` om samma uppdragsnamn finns under flera objekt.
- Enum-värden är toleranta för versaler och å/ä/ö: `Intern|Konsult|Praktikant`, `Forvaltning|Projekt`,
  `Backlogg|Planerad|Pagaende|Pausad|Klar`. Datum skrivs `ÅÅÅÅ-MM-DD`.
- `absence` på en resurs sätter **kapacitet i timmar** en viss månad (t.ex. semester).
- Importen är **allt eller inget**: ett enda fel i filen betyder att ingenting sparas, och felen listas.

## Två sätt att använda filen

**A. Fyll en befintlig installation (rekommenderas när du itererar).** Appen ska köra. Torrkörning först:

```powershell
.\scripts\local.ps1 import data-lokal\verksamhet.json
```

Ser det bra ut, spara på riktigt:

```powershell
.\scripts\local.ps1 import data-lokal\verksamhet.json apply
```

Skriptet frågar efter admin-inloggning. Samma sak finns i API:t: `POST /api/import/json?dryRun=false` (Admin).

**B. Börja från noll med din data istället för demodata.** Tar backup, raderar databasen och läser in filen:

```powershell
.\scripts\local.ps1 load data-lokal\verksamhet.json
```

Det första adminlösenordet blir `admin` (eller `$env:ADMIN_PASSWORD`) och måste bytas. I produktion används miljövariabeln
`SEED_FILE=/data/seed.json`: filen läses in bara när databasen är tom och ignoreras annars.

## Arbeta i gränssnittet och i JSON om vartannat

*Admin → Import/export → Exportera → Verksamhetsdata (JSON)* ger en fil i exakt samma format som importen läser
(alla team, resurser inkl. frånvaro, objekt, uppdrag och fördelning för alla år). Redigera den och läs in den igen under
*Importera JSON* (förhandsgranska, sedan Importera), eller med `local.ps1 import`. Via API: `GET /api/export/dataset.json`.

Tänk på att:

- **Byter du namn** på en resurs, ett uppdrag eller ett objekt i JSON skapas en *ny* post (matchningen sker på namn).
  Byt namn i gränssnittet istället, eller exportera på nytt efteråt.
- **Borttagna rader i JSON raderas inte** i appen. Vill du ta bort något gör du det i gränssnittet, eller sätter `"isActive": false`
  på team, resurser och objekt.
- Fördelning och ändringsloggens historik följer med som den är; importen loggar en rad i ändringsloggen.
- Ta en backup (`local.ps1 backup`) innan stora importer.

## Gott råd

- **Spara filen i git** (men inte persondata du inte får lägga där; håll då repot privat eller `data-lokal/` utanför versionshantering).
  `data-lokal/` är inte ignorerat av git, lägg till den i `.gitignore` om innehållet är känsligt.
- Du kan även lägga in data direkt i appens Admin-vyer eller klistra in från Excel (*Admin → Import*). JSON är bäst för
  grunddata (resurser, objekt, uppdrag, estimat); Excel-inklistring passar fördelningen.
- För att få ut befintlig data: *Admin → Export* (CSV) och *Säkerhetskopia* (hela databasen).
