# Funktionsspecifikation – Resurs & Kapacitet

Framtagen tillsammans med agenten *resursplanerare* (`.claude/agents/resursplanerare.md`).
✅ = finns i version 1.0, ⏳ = förslag till senare version.

## Datamodell

| Entitet | Fält |
|---|---|
| **Resurs** | namn, team, roll/kompetens, typ (intern/konsult), kapacitet h/mån vid 100 % (tomt = standard), sysselsättningsgrad %, start- och slutdatum, aktiv, kommentar |
| **Kapacitetsavvikelse** | resurs, år, månad, tillgängliga timmar, orsak (semester, föräldraledighet, utbildning …) – ersätter beräknad kapacitet den månaden |
| **Team** | namn, teamledare, beskrivning, färg, sortering, aktiv |
| **Förvaltningsobjekt / projekt** | namn, typ, ägare/förvaltningsledare, budget h/år, färg, aktiv |
| **Uppdrag** | namn, objekt, estimat (timmar att fördela), prioritet (rang i backloggen), status (Backlogg / Planerad / Pågående / Pausad / Klar), start/slut, beställare, ansvarig, beskrivning |
| **Allokering** | resurs × uppdrag (en rad i planeringen) + timmar per år och månad, kommentar |
| **Ändringslogg** | tidpunkt, användare, typ, åtgärd, läsbar sammanfattning (t.ex. "Kalle Anka · UppdragX (2026): JAN 100→80") |
| **Användare** | användarnamn, namn, lösenordshash (PBKDF2), roll (Läsare / Planerare / Admin), aktiv, måste byta lösenord |
| **Inställningar** | standardkapacitet, planeringstak, månadsfaktorer, färgtrösklar, kräv inloggning för läsning, lagringstid för logg |
| **Release notes** | `ReleaseNotes.json` i koden – version, datum, rubrik, punkter (Nytt / Förbättrat / Rättat) |

## Beräkningar

- **Kapacitet(r, m)** = (resursens kapacitet ?? standard 160 h) × sysselsättningsgrad × månadsfaktor × planeringstak (90 %).
  Finns en avvikelse används den i stället. Utanför anställningsperioden är kapaciteten 0.
- **Planerat(r, m)** = summan av resursens allokeringar i månaden.
- **Beläggning** = planerat / kapacitet. **Ledigt** = kapacitet − planerat (negativt betyder överbelagd).
- **Kvar att fördela per uppdrag** = estimat − fördelat (alla år). Negativt värde betyder överfördelat.
- **Aggregat** per team, objekt och uppdrag per månad och år.
- **Färgnivåer** (kan ändras i Inställningar):

  | Nivå | Beläggning |
  |---|---|
  | Ledigt | under 80 % |
  | Bra | 80–100 % |
  | Överbelagd | 100–110 % |
  | Kraftigt överbelagd | över 110 % |

  Timmar i en månad där resursen saknar kapacitet räknas alltid som kraftigt överbelagt.

## Standardvärden

- **Kapacitet:** 160 h/mån och 100 % sysselsättning.
- **Planeringstak:** 90 %, vilket ger 144 h/mån för en heltidsresurs.
- **Månadsfaktorer:** juli 0,25, juni och augusti 0,9, december 0,85. Övriga månader 1,0.
- **Hela timmar:** en tom cell betyder 0.
- **Nya backloggposter** hamnar sist i prioritetsordningen.
- **Ett uppdrag i Backlogg** byter status till Planerad när det tilldelas en resurs.

## Funktioner

### MÅSTE

1. ✅ **Fördelningsgrid som i Excel**
   - Redigera direkt i cellen.
   - Navigera med Tab, Enter och piltangenterna.
   - Klistra in block från Excel.
   - Fyll höger med Ctrl+Enter.
   - Ångra med Ctrl+Z.
2. ✅ **Översikt och heatmap** per resurs, team och månad, med väljare för år.
3. ✅ **Uppdragssammanställning** per objekt med estimat, fördelat, kvar och timmar per månad. Varje uppdrag kan expanderas till resurser.
4. ✅ **Backlogg med drag-n-drop**
   - Ändra prioritet genom att dra korten.
   - Dra ett kort till en resurs, eller använd "Tilldela". Det öppnar en dialog med fördelning efter ledig tid, jämnt eller efter kapacitet, och förhandsvisning.
5. ✅ **Admin** för resurser, team, objekt, uppdrag, användare och inställningar. Frånvaro sätts per månad direkt i planeringen.
6. ✅ **Inloggning och roller**
   - Enkel inloggning mot en tabell, med cookie.
   - Optimistisk låsning per cell: om någon annan ändrat samma cell visas deras värde och du kan välja "Använd mina värden".
7. ✅ **Ändringslogg** som kan filtreras och sökas. De senaste ändringarna visas också på startsidan.
8. ✅ **Import och export**
   - Importen tar emot inklistrat från Excel, med förhandsgranskning och felrapport.
   - Exporten ger CSV som Excel kan öppna (semikolon, UTF-8 med BOM).

### BÖR

9. ✅ **Snabbfördelning**
   - Fyll höger.
   - Fördela, viktat efter kapacitet, efter ledig tid eller jämnt.
   - Nollställ rad, med möjlighet att ångra.
   - Flytta en rad till en annan resurs.
   - Kommentar på en rad.
10. ✅ **Filter och sök**
    - Filtrera på team, sök, visa bara överbelagda och visa inaktiva.
    - Sök och team-filter sparas i URL:en.
11. ✅ **Varningar**
    - Överbeläggning.
    - Överfördelade uppdrag.
    - Timmar utanför anställningsperioden.
    - Timmar framåt på uppdrag som är markerade Klar.
12. ✅ **"Räcker kapaciteten?"** jämför det som återstår att fördela med ledig kapacitet per team.
13. ✅ **Nyheter**
    - En badge visar att det finns något nytt sedan sist.
    - Efter en uppgradering visas dialogen "Nytt i version X".
    - En banner visar när en ny version har installerats.
14. ✅ **Responsiv layout** med mörkt och ljust tema.
15. ✅ **Livebevakning** som visar ändringar från andra användare utan att sidan behöver laddas om.

### KAN

16. ✅ **Kopiera ett års plan** till ett annat år.
17. ⏳ Låsa passerade månader.
18. ⏳ Scenarier ("vad händer om") som kan jämföras med basplanen.
19. ⏳ Jämföra plan mot utfall genom import från tidrapporteringen.
20. ⏳ Rullande vy över 18 månader.
21. ⏳ Inloggning via AD/LDAP om kraven på säkerhet ökar.
