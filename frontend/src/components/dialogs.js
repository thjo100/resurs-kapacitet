import { reactive } from 'vue'

// Global bekräftelsedialog: `if (await confirmDialog({...})) { ... }`
export const confirmState = reactive({ open: false, title: '', message: '', confirmLabel: 'OK', danger: false, resolve: null })

export function confirmDialog({ title, message = '', confirmLabel = 'OK', danger = false }) {
  return new Promise((resolve) => {
    Object.assign(confirmState, { open: true, title, message, confirmLabel, danger, resolve })
  })
}

export function closeConfirm(result) {
  confirmState.open = false
  confirmState.resolve?.(result)
  confirmState.resolve = null
}
