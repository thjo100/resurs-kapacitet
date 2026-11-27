<script setup>
import { onMounted } from 'vue'
import { store, markReleasesSeen } from '../store.js'
import { fmtDate } from '../utils/format.js'
import Icon from '../components/Icon.vue'

const typeLabel = { new: 'Nytt', improved: 'Förbättrat', fix: 'Rättat' }
onMounted(markReleasesSeen)
</script>

<template>
  <div class="page narrow">
    <div class="page-head">
      <div>
        <h1>Nyheter</h1>
        <p class="sub">
          Vad som är nytt i Resurs &amp; Kapacitet. Du kör version <strong>{{ store.version?.version }}</strong>
          <template v-if="store.version?.commit"> ({{ store.version.commit.slice(0, 7) }})</template>.
        </p>
      </div>
    </div>
    <div v-if="!store.releases.length" class="empty"><Icon name="sparkles" :size="36" class="icon" /><p>Inga release notes.</p></div>
    <ol class="releases">
      <li v-for="(r, i) in store.releases" :key="r.version" class="card">
        <header>
          <span class="ver" :class="{ latest: i === 0 }">v{{ r.version }}</span>
          <h2>{{ r.title }}</h2>
          <span class="spacer" />
          <time class="muted small">{{ fmtDate(r.date) }}</time>
        </header>
        <ul>
          <li v-for="(it, j) in r.items" :key="j">
            <span class="badge" :class="`nt-${it.type}`">{{ typeLabel[it.type] || it.type }}</span>
            <span>{{ it.text }}</span>
          </li>
        </ul>
      </li>
    </ol>
  </div>
</template>

<style scoped>
.narrow {
  max-width: 860px;
}
.releases {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--sp-4);
}
.releases > li {
  padding: var(--sp-4) var(--sp-5);
}
header {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  flex-wrap: wrap;
  margin-bottom: var(--sp-3);
}
header h2 {
  font-size: var(--fs-lg);
}
.ver {
  font-family: var(--mono);
  font-size: var(--fs-sm);
  font-weight: 700;
  padding: 2px 8px;
  border-radius: var(--r-full);
  background: var(--surface-2);
  color: var(--text-muted);
}
.ver.latest {
  background: var(--accent);
  color: var(--on-accent);
}
ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
ul li {
  display: flex;
  gap: 10px;
  align-items: baseline;
}
.badge {
  flex: none;
  min-width: 78px;
  justify-content: center;
}
.badge.nt-new {
  background: var(--occ-ok-bg);
  color: var(--occ-ok-fg);
}
.badge.nt-fix {
  background: var(--occ-over-bg);
  color: var(--occ-over-fg);
}
.badge.nt-improved {
  background: var(--accent-soft);
  color: var(--accent);
}
</style>
