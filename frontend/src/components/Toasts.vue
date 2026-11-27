<script setup>
import { store, dismissToast } from '../store.js'
import Icon from './Icon.vue'

function run(t) {
  t.action.fn()
  dismissToast(t.id)
}
</script>

<template>
  <div class="toasts">
    <TransitionGroup name="toast">
      <div
        v-for="t in store.toasts"
        :key="t.id"
        class="toast"
        :class="t.type"
        :role="t.type === 'error' ? 'alert' : 'status'"
      >
        <Icon :name="t.type === 'error' ? 'alert' : t.type === 'success' ? 'check' : 'info'" :size="16" />
        <span class="msg">{{ t.message }}</span>
        <button v-if="t.action" class="btn sm" @click="run(t)">{{ t.action.label }}</button>
        <button class="btn ghost icon sm" aria-label="Stäng" @click="dismissToast(t.id)">
          <Icon name="x" :size="14" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toasts {
  position: fixed;
  right: var(--sp-5);
  top: calc(var(--topbar-h) + 12px);
  z-index: 200;
  display: flex;
  flex-direction: column;
  gap: var(--sp-2);
  max-width: min(440px, calc(100vw - 32px));
  pointer-events: none;
}
.toast {
  pointer-events: auto;
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  padding: 10px 8px 10px 14px;
  background: var(--surface);
  color: var(--text);
  border: 1px solid var(--border);
  border-left: 3px solid var(--accent);
  border-radius: var(--r-md);
  box-shadow: var(--sh-2);
  font-size: var(--fs-base);
}
.toast.success {
  border-left-color: var(--occ-ok-bar);
}
.toast.error {
  border-left-color: var(--danger);
}
.toast.error > .icon {
  color: var(--danger);
}
.msg {
  flex: 1;
}
.toast-enter-active,
.toast-leave-active {
  transition:
    opacity var(--dur-3) var(--ease-out),
    transform var(--dur-3) var(--ease-out);
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}
@media (max-width: 767px) {
  .toasts {
    left: 50%;
    right: auto;
    transform: translateX(-50%);
    top: auto;
    bottom: calc(var(--bottomnav-h) + 12px);
    width: calc(100vw - 24px);
  }
}
</style>
