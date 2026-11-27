// Gör om demobygget (dist-demo) till en enda HTML-fil: dist-demo/resurs-kapacitet-demo.html.
// Filen kan öppnas direkt i en webbläsare eller publiceras som en fristående sida.
import { readFileSync, writeFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'

const dir = new URL('../dist-demo/', import.meta.url).pathname
const assets = readdirSync(join(dir, 'assets'))
const js = assets.filter((f) => f.endsWith('.js')).map((f) => readFileSync(join(dir, 'assets', f), 'utf8'))
const css = assets.filter((f) => f.endsWith('.css')).map((f) => readFileSync(join(dir, 'assets', f), 'utf8'))
if (js.length !== 1) throw new Error(`Förväntade ett JS-paket, hittade ${js.length}`)

const favicon = readFileSync(new URL('../public/favicon.svg', import.meta.url), 'utf8')
const html = `<meta charset="utf-8">
<title>Resurs &amp; Kapacitet</title>
<meta name="description" content="Resurs- och kapacitetsplanering för systemutvecklingsteam – demo">
<link rel="icon" href="data:image/svg+xml,${encodeURIComponent(favicon)}">
<style>
${css.join('\n')}
</style>
<script>
  // Följ systemets tema om inget eget val har sparats.
  try { var t = localStorage.getItem('rk-theme'); if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t) } catch (e) {}
</script>
<div id="app"></div>
<script type="module">
${js[0].replace(/<\/script/gi, '<\\/script')}
</script>
`
writeFileSync(join(dir, 'resurs-kapacitet-demo.html'), html)
console.log(`Skrev ${join(dir, 'resurs-kapacitet-demo.html')} (${Math.round(html.length / 1024)} kB)`)
