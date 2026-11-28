<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  store,
  canEdit,
  loadVersion,
  loadPlanning,
  setTheme,
  toggleSidebarPinned,
  toggleTopbarPinned,
  setYear,
  logout,
  startPolling,
  unseenReleases,
  markReleasesSeen,
  toastError,
  toast,
} from './store.js'
import { api } from './api.js'
import { fmtTime } from './utils/format.js'
import { confirmState, closeConfirm } from './components/dialogs.js'
import Icon from './components/Icon.vue'
import Toasts from './components/Toasts.vue'
import Modal from './components/Modal.vue'

// Surfplatta är alltid ikonrad; mobil har bottennavigering (inga dolda menyer).
const mqTablet = window.matchMedia('(min-width: 768px) and (max-width: 1199px)')
const mqMobile = window.matchMedia('(max-width: 767px)')
const isTablet = ref(mqTablet.matches)
const isMobile = ref(mqMobile.matches)
mqTablet.addEventListener('change', (e) => (isTablet.value = e.matches))
mqMobile.addEventListener('change', (e) => (isMobile.value = e.matches))
const sideRail = computed(() => !store.ui.sidebarPinned || isTablet.value)
const topHidden = computed(() => !store.ui.topbarPinned && !isMobile.value)

const route = useRoute()
const router = useRouter()

const nav = computed(() =>
  [
    { to: '/', name: 'overview', label: 'Översikt', icon: 'dashboard' },
    { to: '/planering', name: 'planning', label: 'Planering', icon: 'table' },
    { to: '/backlogg', name: 'backlog', label: 'Backlogg', icon: 'inbox' },
    { to: '/uppdrag', name: 'assignments', label: 'Uppdrag', icon: 'briefcase' },
    { to: '/andringar', name: 'changes', label: 'Ändringslogg', icon: 'clock' },
    { to: '/nyheter', name: 'news', label: 'Nyheter', icon: 'sparkles', dot: unseenReleases.value.length > 0 },
    canEdit.value && { to: '/admin', name: 'admin', label: 'Admin', icon: 'settings' },
  ].filter(Boolean),
)
const bottomNav = computed(() => nav.value.slice(0, 4))
const moreOpen = ref(false)
const userMenu = ref(false)
const pwOpen = ref(false)
const pw = ref({ current: '', next: '', repeat: '' })
const whatsNew = ref(false)

const saveText = computed(() => {
  const s = store.save
  if (s.state === 'pending' || s.state === 'saving') return 'Sparar…'
  if (s.state === 'saved') return `Sparat ✓ ${fmtTime(s.at)}`
  if (s.state === 'error') return 'Ej sparat'
  return ''
})

const envLabel = computed(() => store.version?.environment)

onMounted(async () => {
  await loadVersion()
  // Efter en uppgradering visas "Nytt i version X" en gång.
  if (store.lastSeenVersion && unseenReleases.value.length) whatsNew.value = true
  startPolling()
})

watch(
  () => store.year,
  (y) => loadPlanning(y),
  { immediate: true },
)

watch(
  () => store.me?.mustChangePassword,
  (must) => {
    if (must) pwOpen.value = true
  },
  { immediate: true },
)

function closeWhatsNew() {
  whatsNew.value = false
  markReleasesSeen()
}

async function changePassword() {
  if (pw.value.next !== pw.value.repeat) return toast('Lösenorden matchar inte.', { type: 'error' })
  try {
    await api.post('/api/auth/password', { currentPassword: pw.value.current, newPassword: pw.value.next })
    store.me.mustChangePassword = false
    pwOpen.value = false
    pw.value = { current: '', next: '', repeat: '' }
    toast('Lösenordet är bytt.', { type: 'success' })
  } catch (e) {
    toastError(e)
  }
}

async function doLogout() {
  userMenu.value = false
  moreOpen.value = false
  await logout()
  if (route.meta.requiresRole) router.push('/')
}

const reload = () => window.location.reload()
const isDemo = import.meta.env.VITE_DEMO === 'true'
async function resetDemo() {
  userMenu.value = false
  moreOpen.value = false
  await api.post('/api/demo/reset')
  store.me = null
  await loadPlanning(store.year)
  router.push('/')
  toast('Demodatan är återställd.', { type: 'success' })
}
const toggleTheme = () => setTheme(store.theme === 'dark' ? 'light' : 'dark')
const isActive = (item) => (item.name === 'admin' ? route.name === 'admin' : route.name === item.name)
</script>

<template>
  <div class="shell" :class="{ rail: sideRail, 'top-hidden': topHidden }">
    <div v-if="topHidden" class="top-hot">
      <button class="top-tab" aria-label="Visa och fäst översta menyn" title="Fäst översta menyn" @click="toggleTopbarPinned">
        <Icon name="chevron-down" :size="14" />
      </button>
    </div>
    <header class="topbar">
      <RouterLink to="/" class="brand" aria-label="Resurs & Kapacitet – startsida">
        <svg viewBox="0 0 32 32" width="26" height="26" aria-hidden="true">
          <rect width="32" height="32" rx="7" fill="var(--accent)" />
          <rect x="6" y="18" width="4" height="8" rx="1" fill="var(--on-accent)" />
          <rect x="12" y="12" width="4" height="14" rx="1" fill="var(--on-accent)" />
          <rect x="18" y="8" width="4" height="18" rx="1" fill="var(--on-accent)" opacity=".85" />
          <rect x="24" y="14" width="3" height="12" rx="1" fill="var(--on-accent)" opacity=".7" />
        </svg>
        <span class="brand-text">Resurs <span class="amp">&amp;</span> Kapacitet</span>
      </RouterLink>
      <span v-if="envLabel" class="env" :title="`Testmiljö: ${envLabel}`">{{ envLabel }}</span>

      <div class="year" role="group" aria-label="Välj år">
        <button class="btn ghost icon sm" aria-label="Föregående år" @click="setYear(store.year - 1)">
          <Icon name="chevron-left" :size="16" />
        </button>
        <span class="num year-num" aria-live="polite">{{ store.year }}</span>
        <button class="btn ghost icon sm" aria-label="Nästa år" @click="setYear(store.year + 1)">
          <Icon name="chevron-right" :size="16" />
        </button>
      </div>

      <div class="spacer" />

      <span
        v-if="saveText"
        class="save-state"
        :class="store.save.state"
        role="status"
        :title="store.save.error || ''"
      >
        <span v-if="store.save.state === 'saving' || store.save.state === 'pending'" class="spinner" />
        {{ saveText }}
      </span>

      <button
        class="btn ghost icon hide-sm"
        :aria-label="store.ui.topbarPinned ? 'Dölj översta menyn' : 'Fäst översta menyn'"
        :title="store.ui.topbarPinned ? 'Dölj översta menyn (visas när du för musen till kanten)' : 'Fäst översta menyn'"
        @click="toggleTopbarPinned"
      >
        <Icon :name="store.ui.topbarPinned ? 'chevron-up' : 'pin'" />
      </button>
      <button class="btn ghost icon hide-sm" :aria-label="store.theme === 'dark' ? 'Ljust tema' : 'Mörkt tema'" @click="toggleTheme">
        <Icon :name="store.theme === 'dark' ? 'sun' : 'moon'" />
      </button>

      <div v-if="store.me" class="user">
        <button class="btn ghost user-btn" :aria-expanded="userMenu" @click="userMenu = !userMenu">
          <span class="avatar">{{ store.me.displayName.slice(0, 1) }}</span>
          <span class="hide-sm">{{ store.me.displayName }}</span>
        </button>
        <Transition name="pop">
          <div v-if="userMenu" class="menu" @mouseleave="userMenu = false">
            <div class="menu-head">
              <strong>{{ store.me.displayName }}</strong>
              <span class="muted small">{{ store.me.userName }} · {{ store.me.role === 'Lasare' ? 'Läsare' : store.me.role }}</span>
            </div>
            <button class="menu-item" @click="((pwOpen = true), (userMenu = false))"><Icon name="key" :size="16" /> Byt lösenord</button>
            <button v-if="isDemo" class="menu-item" @click="resetDemo"><Icon name="refresh" :size="16" /> Återställ demodata</button>
            <button class="menu-item" @click="doLogout"><Icon name="logout" :size="16" /> Logga ut</button>
          </div>
        </Transition>
      </div>
      <RouterLink v-else class="btn sm" :to="{ name: 'login', query: { till: route.fullPath } }">
        <Icon name="login" :size="16" /><span class="hide-sm">Logga in</span>
      </RouterLink>
    </header>

    <nav class="sidebar" aria-label="Huvudmeny">
      <RouterLink
        v-for="item in nav"
        :key="item.to"
        :to="item.to"
        class="nav-item"
        :class="{ active: isActive(item) }"
        :title="item.label"
      >
        <Icon :name="item.icon" />
        <span class="label">{{ item.label }}</span>
        <span v-if="item.dot" class="dot" aria-label="Nyheter finns" />
      </RouterLink>
      <div class="spacer" />
      <button
        v-if="!isTablet"
        class="nav-item pin-btn"
        :title="store.ui.sidebarPinned ? 'Minimera menyn' : 'Fäst menyn öppen'"
        :aria-pressed="store.ui.sidebarPinned"
        @click="toggleSidebarPinned"
      >
        <Icon :name="store.ui.sidebarPinned ? 'panel-left' : 'pin'" />
        <span class="label">{{ store.ui.sidebarPinned ? 'Minimera menyn' : 'Fäst menyn' }}</span>
      </button>
      <div class="side-foot">
        <span class="faint xs" :title="store.version?.commit ? `Commit ${store.version.commit}` : ''">
          v{{ store.version?.version ?? '–' }}
        </span>
      </div>
    </nav>

    <main class="main">
      <Transition name="slide-down">
        <div v-if="store.newVersion" class="banner info" role="status">
          <Icon name="sparkles" :size="16" />
          <span>En ny version ({{ store.newVersion.version }}) är installerad.</span>
          <button class="btn sm primary" @click="reload">Ladda om</button>
        </div>
      </Transition>
      <RouterView v-slot="{ Component }">
        <Transition name="page" mode="out-in">
          <component :is="Component" />
        </Transition>
      </RouterView>
    </main>

    <nav class="bottomnav" aria-label="Huvudmeny">
      <RouterLink v-for="item in bottomNav" :key="item.to" :to="item.to" class="bn-item" :class="{ active: isActive(item) }">
        <Icon :name="item.icon" :size="20" />
        <span>{{ item.label }}</span>
      </RouterLink>
      <button class="bn-item" :class="{ active: moreOpen }" @click="moreOpen = true">
        <span class="rel"><Icon name="menu" :size="20" /><span v-if="unseenReleases.length" class="dot" /></span>
        <span>Mer</span>
      </button>
    </nav>

    <Modal v-if="moreOpen" title="Mer" @close="moreOpen = false">
      <div class="more-list">
        <RouterLink v-for="item in nav.slice(4)" :key="item.to" :to="item.to" class="menu-item" @click="moreOpen = false">
          <Icon :name="item.icon" :size="18" /> {{ item.label }}
          <span v-if="item.dot" class="dot" />
        </RouterLink>
        <button class="menu-item" @click="toggleTheme">
          <Icon :name="store.theme === 'dark' ? 'sun' : 'moon'" :size="18" />
          {{ store.theme === 'dark' ? 'Ljust tema' : 'Mörkt tema' }}
        </button>
        <button v-if="isDemo" class="menu-item" @click="resetDemo"><Icon name="refresh" :size="18" /> Återställ demodata</button>
        <button v-if="store.me" class="menu-item" @click="doLogout"><Icon name="logout" :size="18" /> Logga ut</button>
        <RouterLink v-else class="menu-item" to="/logga-in" @click="moreOpen = false"><Icon name="login" :size="18" /> Logga in</RouterLink>
        <p class="faint xs" style="padding: 8px 12px">Version {{ store.version?.version }}</p>
      </div>
    </Modal>

    <Modal v-if="whatsNew && store.releases[0]" :title="`Nytt i version ${store.releases[0].version}`" @close="closeWhatsNew">
      <p class="muted" style="margin-bottom: 12px">{{ store.releases[0].title }}</p>
      <ul class="news-list">
        <li v-for="(it, i) in store.releases[0].items.slice(0, 5)" :key="i">
          <span class="badge" :class="`nt-${it.type}`">{{ { new: 'Nytt', fix: 'Rättat', improved: 'Förbättrat' }[it.type] || it.type }}</span>
          {{ it.text }}
        </li>
      </ul>
      <template #footer>
        <RouterLink to="/nyheter" class="btn" @click="closeWhatsNew">Visa alla nyheter</RouterLink>
        <button class="btn primary" @click="closeWhatsNew">Okej!</button>
      </template>
    </Modal>

    <Modal v-if="pwOpen" title="Byt lösenord" :subtitle="store.me?.mustChangePassword ? 'Du använder ett standardlösenord – byt det innan du fortsätter.' : ''" width="420px" @close="pwOpen = false">
      <form id="pwform" class="stack" @submit.prevent="changePassword">
        <label class="field"><span>Nuvarande lösenord</span><input v-model="pw.current" class="input" type="password" autocomplete="current-password" required /></label>
        <label class="field"><span>Nytt lösenord (minst 6 tecken)</span><input v-model="pw.next" class="input" type="password" autocomplete="new-password" minlength="6" required /></label>
        <label class="field"><span>Upprepa nytt lösenord</span><input v-model="pw.repeat" class="input" type="password" autocomplete="new-password" minlength="6" required /></label>
      </form>
      <template #footer>
        <button class="btn" @click="pwOpen = false">Avbryt</button>
        <button class="btn primary" type="submit" form="pwform">Byt lösenord</button>
      </template>
    </Modal>

    <Modal v-if="confirmState.open" :title="confirmState.title" width="440px" @close="closeConfirm(false)">
      <p style="white-space: pre-line">{{ confirmState.message }}</p>
      <template #footer>
        <button class="btn" @click="closeConfirm(false)">Avbryt</button>
        <button class="btn" :class="confirmState.danger ? 'danger solid' : 'primary'" autofocus @click="closeConfirm(true)">
          {{ confirmState.confirmLabel }}
        </button>
      </template>
    </Modal>

    <Toasts />
  </div>
</template>

<style scoped>
.shell {
  min-height: 100vh;
  display: grid;
  grid-template-columns: var(--sidebar-w) minmax(0, 1fr);
  grid-template-rows: var(--topbar-h) minmax(0, 1fr);
  grid-template-areas:
    'top top'
    'side main';
}
.topbar {
  grid-area: top;
  position: sticky;
  top: env(safe-area-inset-top, 0px);
  z-index: 50;
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  padding: 0 var(--sp-4);
  background: var(--surface);
  border-bottom: 1px solid var(--border);
}
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  color: var(--text);
  text-decoration: none;
  font-weight: 700;
  font-size: var(--fs-lg);
  letter-spacing: -0.01em;
  margin-right: var(--sp-2);
}
.amp {
  color: var(--accent);
}
.env {
  background: var(--occ-near-bg);
  color: var(--occ-near-fg);
  border: 1px solid var(--occ-near-bar);
  font-size: var(--fs-xs);
  font-weight: 700;
  padding: 2px 8px;
  border-radius: var(--r-full);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  white-space: nowrap;
}
.year {
  display: flex;
  align-items: center;
  gap: 2px;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  padding: 1px;
}
.year-num {
  font-weight: 700;
  min-width: 44px;
  text-align: center;
}
.save-state {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: var(--fs-sm);
  color: var(--text-muted);
  white-space: nowrap;
}
.save-state.error {
  color: var(--danger);
  font-weight: 600;
}
.spinner {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  border: 2px solid var(--border-strong);
  border-top-color: var(--accent);
  animation: spin 0.7s linear infinite;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
.user {
  position: relative;
}
.user-btn {
  gap: 8px;
}
.avatar {
  width: 26px;
  height: 26px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--accent-soft);
  color: var(--accent);
  font-weight: 700;
  font-size: var(--fs-sm);
}
.menu {
  position: absolute;
  right: 0;
  top: calc(100% + 6px);
  min-width: 220px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  box-shadow: var(--sh-2);
  padding: 6px;
  z-index: 60;
}
.menu-head {
  display: flex;
  flex-direction: column;
  padding: 8px 10px 10px;
  border-bottom: 1px solid var(--border);
  margin-bottom: 4px;
}
.menu-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 10px 12px;
  border: 0;
  background: transparent;
  border-radius: var(--r-sm);
  font: inherit;
  color: var(--text);
  text-decoration: none;
  cursor: pointer;
  text-align: left;
}
.menu-item:hover {
  background: var(--surface-2);
}
.sidebar {
  grid-area: side;
  position: sticky;
  top: var(--topbar-h);
  height: calc(100vh - var(--topbar-h));
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: var(--sp-3) var(--sp-2);
  background: var(--surface);
  border-right: 1px solid var(--border);
}
.nav-item {
  position: relative;
  display: flex;
  align-items: center;
  gap: 12px;
  height: 38px;
  padding: 0 12px;
  border-radius: var(--r-md);
  color: var(--text-muted);
  text-decoration: none;
  font-weight: 550;
  transition:
    background var(--dur-1),
    color var(--dur-1);
}
.nav-item:hover {
  background: var(--surface-2);
  color: var(--text);
}
.nav-item.active {
  background: var(--accent-soft);
  color: var(--accent);
}
.nav-item.active::before {
  content: '';
  position: absolute;
  left: -8px;
  top: 8px;
  bottom: 8px;
  width: 3px;
  border-radius: 0 3px 3px 0;
  background: var(--accent);
}
.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--danger);
  margin-left: auto;
}
.side-foot {
  padding: 8px 12px;
}
.main {
  grid-area: main;
  min-width: 0;
  padding-bottom: var(--bottomnav-h);
}
.banner {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  padding: 8px var(--sp-5);
  background: var(--accent-soft);
  color: var(--text);
  border-bottom: 1px solid var(--border);
}
.bottomnav {
  display: none;
}
.more-list {
  display: flex;
  flex-direction: column;
}
.news-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.news-list li {
  display: flex;
  gap: 10px;
  align-items: baseline;
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
.rel {
  position: relative;
  display: inline-flex;
}
.rel .dot {
  position: absolute;
  top: -2px;
  right: -4px;
}

/* Indragen meny (ikonrad): expanderar över innehållet vid hover/fokus. Gäller alltid på surfplatta. */
.shell.rail {
  --sidebar-w: 64px;
}
.shell.rail .sidebar {
  z-index: 40;
  width: 64px;
  overflow: hidden;
  transition:
    width var(--dur-2) var(--ease-out),
    box-shadow var(--dur-2) var(--ease-out);
}
.shell.rail .sidebar:hover,
.shell.rail .sidebar:has(:focus-visible) {
  width: var(--sidebar-full-open, 224px);
  box-shadow: var(--sh-2);
}
.shell.rail .nav-item {
  justify-content: flex-start;
  white-space: nowrap;
}
.shell.rail .nav-item .dot {
  position: absolute;
  top: 6px;
  left: 30px;
  margin: 0;
}
.shell.rail .sidebar:not(:hover):not(:has(:focus-visible)) .side-foot {
  visibility: hidden;
}
.pin-btn {
  border: 0;
  background: transparent;
  font: inherit;
  cursor: pointer;
  width: 100%;
  text-align: left;
}
.nav-item :deep(svg) {
  flex: none;
}
.nav-item .label {
  white-space: nowrap;
}
.shell.rail .sidebar:not(:hover):not(:has(:focus-visible)) .label {
  opacity: 0;
}

/* Översta menyn indragen (bara surfplatta/dator): visas som överlägg när musen når kanten. */
@media (min-width: 768px) {
  .shell.top-hidden {
    --topbar-h: 0px;
  }
  .shell.top-hidden .topbar {
    position: fixed;
    left: 0;
    right: 0;
    top: 0;
    transform: translateY(-101%);
    height: var(--topbar-full);
    box-shadow: var(--sh-2);
    transition: transform var(--dur-2) var(--ease-out);
  }
  .top-hot:hover ~ .topbar,
  .shell.top-hidden .topbar:hover,
  .shell.top-hidden .topbar:has(:focus-visible) {
    transform: none;
  }
  .top-hot {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    height: 6px;
    z-index: 55;
  }
  .top-tab {
    position: absolute;
    left: 50%;
    top: 0;
    transform: translateX(-50%);
    width: 44px;
    height: 14px;
    padding: 0;
    display: grid;
    place-items: center;
    border: 1px solid var(--border);
    border-top: 0;
    border-radius: 0 0 var(--r-md) var(--r-md);
    background: var(--surface);
    color: var(--text-muted);
    cursor: pointer;
    opacity: 0.6;
  }
  .top-tab:hover {
    opacity: 1;
    color: var(--accent);
  }
}

/* Mobil: bottennavigering */
@media (max-width: 767px) {
  .shell {
    grid-template-columns: minmax(0, 1fr);
    grid-template-areas:
      'top'
      'main';
  }
  .sidebar,
  .hide-sm,
  .brand-text {
    display: none;
  }
  .topbar {
    gap: var(--sp-2);
    padding: 0 var(--sp-3);
  }
  .bottomnav {
    display: flex;
    position: fixed;
    bottom: 0;
    left: 0;
    right: 0;
    z-index: 50;
    height: var(--bottomnav-h);
    padding-bottom: env(safe-area-inset-bottom);
    background: var(--surface);
    border-top: 1px solid var(--border);
  }
  .bn-item {
    flex: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 2px;
    border: 0;
    background: transparent;
    color: var(--text-muted);
    font: inherit;
    font-size: var(--fs-xs);
    font-weight: 600;
    text-decoration: none;
  }
  .bn-item.active {
    color: var(--accent);
  }
  .save-state {
    font-size: var(--fs-xs);
  }
}

.page-enter-active,
.page-leave-active {
  transition:
    opacity var(--dur-2) var(--ease-out),
    transform var(--dur-2) var(--ease-out);
}
.page-enter-from {
  opacity: 0;
  transform: translateY(6px);
}
.page-leave-to {
  opacity: 0;
}
.pop-enter-active,
.pop-leave-active {
  transition:
    opacity var(--dur-2) var(--ease-out),
    transform var(--dur-2) var(--ease-out);
}
.pop-enter-from,
.pop-leave-to {
  opacity: 0;
  transform: translateY(-4px) scale(0.98);
}
.slide-down-enter-active,
.slide-down-leave-active {
  transition: all var(--dur-3) var(--ease-out);
}
.slide-down-enter-from,
.slide-down-leave-to {
  opacity: 0;
  transform: translateY(-100%);
}
</style>
