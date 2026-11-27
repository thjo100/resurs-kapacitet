<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { api } from '../api.js'
import { store } from '../store.js'
import { fmtDateTime, fmtRelative } from '../utils/format.js'
import Icon from '../components/Icon.vue'

const items = ref([])
const loading = ref(false)
const done = ref(false)
const q = ref('')
const type = ref('')
const resourceId = ref('')
const types = ['Allokering', 'Uppdrag', 'Backlogg', 'Resurs', 'Kapacitet', 'Team', 'Objekt', 'Användare', 'Inställningar', 'Import', 'Planering']

const icons = {
  Allokering: 'table',
  Uppdrag: 'briefcase',
  Backlogg: 'inbox',
  Resurs: 'user',
  Kapacitet: 'calendar',
  Team: 'users',
  Objekt: 'folder',
  Användare: 'key',
  Inställningar: 'settings',
  Import: 'upload',
  Planering: 'copy',
}

let timer
async function load(reset = true) {
  loading.value = true
  try {
    const params = new URLSearchParams({ take: '60' })
    if (q.value) params.set('q', q.value)
    if (type.value) params.set('entityType', type.value)
    if (resourceId.value) params.set('resourceId', resourceId.value)
    if (!reset && items.value.length) params.set('before', String(items.value[items.value.length - 1].id))
    const res = await api.get(`/api/changes?${params}`)
    items.value = reset ? res : [...items.value, ...res]
    done.value = res.length < 60
  } finally {
    loading.value = false
  }
}
onMounted(load)
watch([type, resourceId], () => load())
watch(q, () => {
  clearTimeout(timer)
  timer = setTimeout(load, 300)
})
watch(() => store.planning?.lastChangeId, () => load())

const grouped = computed(() => {
  const out = []
  let day = null
  for (const c of items.value) {
    const d = new Date(c.timestamp).toLocaleDateString('sv-SE', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
    if (d !== day) {
      out.push({ day: d, items: [] })
      day = d
    }
    out[out.length - 1].items.push(c)
  }
  return out
})
</script>

<template>
  <div class="page">
    <div class="page-head">
      <div>
        <h1>Ändringslogg</h1>
        <p class="sub">Vem ändrade vad och när. Uppdateras automatiskt.</p>
      </div>
    </div>
    <div class="filters">
      <div class="search">
        <Icon name="search" :size="14" />
        <input v-model="q" class="input" type="search" placeholder="Sök i ändringar eller användare…" aria-label="Sök i ändringsloggen" />
      </div>
      <select v-model="type" class="input" aria-label="Typ" style="width: 170px">
        <option value="">Alla typer</option>
        <option v-for="t in types" :key="t" :value="t">{{ t }}</option>
      </select>
      <select v-model="resourceId" class="input" aria-label="Resurs" style="width: 200px">
        <option value="">Alla resurser</option>
        <option v-for="r in store.planning?.resources ?? []" :key="r.id" :value="r.id">{{ r.name }}</option>
      </select>
    </div>

    <div class="card">
      <div v-if="!items.length && loading" class="card-body stack">
        <div v-for="i in 6" :key="i" class="skeleton" style="height: 44px" />
      </div>
      <div v-else-if="!items.length" class="empty">
        <Icon name="clock" :size="36" class="icon" />
        <h3>Inga ändringar</h3>
        <p>Inget matchar filtret.</p>
      </div>
      <template v-for="g in grouped" :key="g.day">
        <h3 class="day">{{ g.day }}</h3>
        <ul class="timeline">
          <li v-for="c in g.items" :key="c.id">
            <span class="ico" :class="`a-${c.action}`"><Icon :name="icons[c.entityType] || 'info'" :size="14" /></span>
            <div class="txt">
              <p>{{ c.summary }}</p>
              <span class="faint xs">
                <strong class="muted">{{ c.user }}</strong> · {{ c.entityType }} · {{ c.action }} ·
                <time :datetime="c.timestamp" :title="fmtDateTime(c.timestamp)">{{ fmtRelative(c.timestamp) }}</time>
              </span>
            </div>
          </li>
        </ul>
      </template>
      <div v-if="items.length && !done" class="more">
        <button class="btn" :disabled="loading" @click="load(false)">Visa äldre</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.filters {
  display: flex;
  gap: var(--sp-2);
  flex-wrap: wrap;
  margin-bottom: var(--sp-3);
}
.search {
  position: relative;
  display: flex;
  align-items: center;
  flex: 1;
  min-width: 220px;
  max-width: 400px;
}
.search > .icon {
  position: absolute;
  left: 10px;
  color: var(--text-faint);
  pointer-events: none;
}
.search .input {
  padding-left: 30px;
}
.day {
  font-size: var(--fs-sm);
  text-transform: capitalize;
  color: var(--text-muted);
  padding: var(--sp-3) var(--sp-4) var(--sp-1);
  position: sticky;
  top: var(--topbar-h);
  background: var(--surface);
  z-index: 1;
}
.timeline {
  list-style: none;
  margin: 0;
  padding: 0 var(--sp-4) var(--sp-2);
}
.timeline li {
  display: flex;
  gap: 12px;
  padding: 10px 0;
  border-bottom: 1px solid var(--border);
}
.timeline li:last-child {
  border-bottom: 0;
}
.ico {
  flex: none;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--surface-2);
  color: var(--text-muted);
}
.ico.a-Skapad,
.ico.a-Tilldelad {
  background: var(--occ-ok-bg);
  color: var(--occ-ok-fg);
}
.ico.a-Borttagen {
  background: var(--occ-over-bg);
  color: var(--occ-over-fg);
}
.ico.a-Flyttad,
.ico.a-Omprioriterad {
  background: var(--accent-soft);
  color: var(--accent);
}
.txt {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.txt p {
  font-size: var(--fs-table);
  overflow-wrap: anywhere;
}
.more {
  display: flex;
  justify-content: center;
  padding: var(--sp-3);
  border-top: 1px solid var(--border);
}
</style>
