<script setup>
// Dialog: centrerad på stora skärmar, "bottom sheet" på mobil. variant="drawer" ger en panel från höger.
import { onBeforeUnmount, onMounted, ref, nextTick } from 'vue'
import Icon from './Icon.vue'

const props = defineProps({
  title: { type: String, default: '' },
  subtitle: { type: String, default: '' },
  variant: { type: String, default: 'dialog' }, // dialog | drawer
  width: { type: String, default: '560px' },
})
const emit = defineEmits(['close'])
const panel = ref(null)
let lastFocus = null

function onKey(e) {
  if (e.key === 'Escape') {
    e.stopPropagation()
    emit('close')
  }
  if (e.key === 'Tab' && panel.value) {
    const f = panel.value.querySelectorAll(
      'button:not([disabled]), [href], input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])',
    )
    if (!f.length) return
    const first = f[0]
    const last = f[f.length - 1]
    if (e.shiftKey && document.activeElement === first) {
      last.focus()
      e.preventDefault()
    } else if (!e.shiftKey && document.activeElement === last) {
      first.focus()
      e.preventDefault()
    }
  }
}

onMounted(async () => {
  lastFocus = document.activeElement
  document.body.style.overflow = 'hidden'
  await nextTick()
  const auto = panel.value?.querySelector('[autofocus], input, select, textarea, .btn.primary')
  ;(auto || panel.value)?.focus()
})
onBeforeUnmount(() => {
  document.body.style.overflow = ''
  lastFocus?.focus?.()
})
</script>

<template>
  <Teleport to="body">
    <div class="backdrop" :class="variant" @mousedown.self="emit('close')" @keydown="onKey">
      <div
        ref="panel"
        class="panel"
        role="dialog"
        aria-modal="true"
        :aria-label="title"
        tabindex="-1"
        :style="{ '--w': props.width }"
      >
        <header class="head">
          <div class="titles">
            <h2>{{ title }}</h2>
            <p v-if="subtitle" class="muted small">{{ subtitle }}</p>
          </div>
          <slot name="head-extra" />
          <button class="btn ghost icon" aria-label="Stäng" @click="emit('close')"><Icon name="x" /></button>
        </header>
        <div class="body"><slot /></div>
        <footer v-if="$slots.footer" class="foot"><slot name="footer" /></footer>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.backdrop {
  position: fixed;
  inset: 0;
  z-index: 100;
  background: rgba(10, 14, 20, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--sp-4);
  animation: fade var(--dur-2) var(--ease-out);
}
.panel {
  width: min(var(--w), 100%);
  max-height: min(90vh, 900px);
  display: flex;
  flex-direction: column;
  background: var(--surface);
  border-radius: var(--r-lg);
  box-shadow: var(--sh-3);
  border: 1px solid var(--border);
  animation: pop var(--dur-3) var(--ease-out);
  outline: none;
}
.backdrop.drawer {
  justify-content: flex-end;
  align-items: stretch;
  padding: 0;
}
.drawer .panel {
  width: min(var(--w), 100%);
  max-height: none;
  height: 100%;
  border-radius: 0;
  border-width: 0 0 0 1px;
  animation: slide var(--dur-3) var(--ease-out);
}
.head {
  display: flex;
  align-items: flex-start;
  gap: var(--sp-2);
  padding: var(--sp-4) var(--sp-4) var(--sp-3) var(--sp-5);
  border-bottom: 1px solid var(--border);
}
.titles {
  flex: 1;
  min-width: 0;
}
.head h2 {
  font-size: var(--fs-lg);
  padding-top: 6px;
}
.body {
  padding: var(--sp-4) var(--sp-5);
  overflow: auto;
  flex: 1;
}
.foot {
  display: flex;
  gap: var(--sp-2);
  justify-content: flex-end;
  flex-wrap: wrap;
  padding: var(--sp-3) var(--sp-5);
  border-top: 1px solid var(--border);
  background: var(--surface-2);
  border-radius: 0 0 var(--r-lg) var(--r-lg);
}
@media (max-width: 767px) {
  .backdrop,
  .backdrop.drawer {
    align-items: flex-end;
    padding: 0;
  }
  .panel,
  .drawer .panel {
    width: 100%;
    height: auto;
    max-height: 88vh;
    border-radius: var(--r-lg) var(--r-lg) 0 0;
    border-width: 1px 0 0;
    animation: up var(--dur-3) var(--ease-out);
    padding-bottom: env(safe-area-inset-bottom);
  }
  .body {
    padding: var(--sp-4);
  }
  .foot {
    border-radius: 0;
  }
}
@keyframes fade {
  from {
    opacity: 0;
  }
}
@keyframes pop {
  from {
    opacity: 0;
    transform: translateY(8px) scale(0.98);
  }
}
@keyframes slide {
  from {
    transform: translateX(40px);
    opacity: 0;
  }
}
@keyframes up {
  from {
    transform: translateY(40px);
    opacity: 0;
  }
}
</style>
