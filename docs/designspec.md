# Designspecifikation – Resurs & Kapacitet

Specifikationen är framtagen av agenten *ux-designer* (`.claude/agents/ux-designer.md`). Den är implementerad i
`frontend/src/styles/tokens.css`, `base.css`, `grid.css` och komponenterna under `frontend/src/components`.

## Layout och navigering

- **Brytpunkter:** `< 768px` mobil, `768–1199px` surfplatta, `≥ 1200px` dator.
- **Topbar:** sticky, 52 px hög. Den innehåller appnamn, en etikett för testmiljön (t.ex. "PR #12"), årväljare, sparat-indikator, temaväxlare och användarmeny.
- **Sidomeny:** 224 px bred på dator. På surfplatta krymper den till en ikonrad på 64 px.
- **Mobil:** bottennavigering med Översikt, Planering, Backlogg, Uppdrag och Mer. Dialoger visas som bottom sheets.
- **Planeringsgrid:** byggs med CSS grid och inte `<table>`, eftersom expand/collapse-animationen kräver det.
  - Namnkolumnen och rubrikraden är sticky.
  - Totalraden ligger sticky i botten.
  - När innehållet scrollas i sidled får namnkolumnen en skugga.

## Tokens

Alla färger är CSS-variabler och omdefinieras under `:root[data-theme='dark']`. Temat sätts av ett inline-script i `index.html` innan appen
monteras, så att fel tema aldrig blinkar till.

| Beläggningsnivå | Ljust (bg / fg) | Mörkt (bg / fg) |
|---|---|---|
| Ledigt (`low`) | `#eef1f5` / `#4a5361` | `#1e232b` / `#a3acb9` |
| Bra (`ok`) | `#e3f4e8` / `#1d6b35` | `#12301e` / `#7ddc9b` |
| Överbelagd (`near`) | `#fdf1d8` / `#8a5a00` | `#3a2c0b` / `#f5c25b` |
| Kraftigt överbelagd (`over`) | `#fde4e4` / `#a8201f` | `#431819` / `#ff8a87` |

**Animationstider:** `--dur-1` 120 ms, `--dur-2` 200 ms och `--dur-3` 300 ms, med `--ease-out: cubic-bezier(.2,0,0,1)`.
Vid `prefers-reduced-motion` sätts tiderna till 0,01 ms och inte till 0, så att `transitionend` fortfarande avfyras.

## Beläggningscell (heatmap)

Varje cell visar timmar, procent eller ledig tid, beroende på vilken visning som är vald. I botten finns en 3 px stapel.

- **Överbeläggning** markeras med en liten ▲ och fetare siffror, så att status aldrig bara visas med färg.
- **Kapacitet 0** (frånvaro eller utanför anställningen) visas med streckad bakgrund.
- **Tooltip:** "Kalle Anka, mars: 150 av 144 h (104 %), 6 h för mycket – överbelagd".

## Expand/collapse

```css
.expander { display: grid; grid-template-rows: 0fr; transition: grid-template-rows var(--dur-3) var(--ease-out); }
.expander.open { grid-template-rows: 1fr; }
.expander > .inner { min-height: 0; overflow-y: clip; }
```

- Använd `overflow-y: clip` och inte `hidden`. Med `hidden` slutar den sticky namnkolumnen att fungera i de expanderade raderna.
- Innehållet renderas bara när blocket är öppet eller håller på att stängas.
- Chevronen roteras 90°.
- Knappen har `aria-expanded` och `aria-controls`.

## Drag-n-drop

- **Teknik:** SortableJS i fallback-läge med pekarhändelser, så att det fungerar med både mus och touch.
  - Med mus startar draget efter 4 px rörelse.
  - På touch krävs ett långt tryck på 300 ms på handtaget ⠿.
- **Ghost:** följer pekaren med en lätt rotation och skugga.
- **Drop-mål:** resursraden får en streckad accentram, och etiketten "Släpp för att tilldela {namn}" visas.
- **Efter drop:** tilldelningsdialogen öppnas. När tilldelningen är klar expanderas resursen och den nya raden blinkar.
- **Alternativ:** knappen "Tilldela" finns på varje kort. Den är huvudvägen på mobil och för tangentbord.

## Fördelningspanel

Fördelningspanelen ligger i botten av planeringen. Den visar alltid den aktiva radens uppdrag:

- estimat
- fördelat, med en progress-stapel
- **kvar att fördela** i stor stil: gul om det finns kvar, grön med ✓ vid 0 och röd när uppdraget är överfördelat
- resursens lediga tid i den valda månaden.

Panelen har snabbknappar för Fyll höger, Fördela… och Nollställ.

## Mikrointeraktioner

- **Autospar:** sparar 600 ms efter senaste ändringen. Topbaren visar "Sparar…", sedan "Sparat ✓ 14:32" eller "Ej sparat".
- **Toasts:** visas uppe till höger, på mobil ovanför bottennavigeringen. Fel stannar kvar tills de stängs. Toasts kan ha en åtgärd, som "Ångra" eller "Försök igen".
- **Skeleton:** platshållare med shimmer visas medan data laddas.
- **Tomma tillstånd:** ikon, rubrik och en knapp för nästa steg.
