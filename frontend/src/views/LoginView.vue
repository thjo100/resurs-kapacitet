<script setup>
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { login, toast, store } from '../store.js'

const route = useRoute()
const router = useRouter()
const userName = ref('')
const password = ref('')
const error = ref('')
const busy = ref(false)

async function submit() {
  busy.value = true
  error.value = ''
  try {
    const me = await login(userName.value, password.value)
    toast(`Välkommen, ${me.displayName}!`, { type: 'success' })
    router.replace(String(route.query.till || '/planering'))
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <form class="card login" @submit.prevent="submit">
      <h1>Logga in</h1>
      <p class="muted">Logga in för att planera och administrera. Alla kan se planeringen utan inloggning.</p>
      <label class="field"><span>Användarnamn</span><input v-model="userName" class="input" autocomplete="username" autofocus required /></label>
      <label class="field"><span>Lösenord</span><input v-model="password" class="input" type="password" autocomplete="current-password" required /></label>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
      <button class="btn primary" type="submit" :disabled="busy">{{ busy ? 'Loggar in…' : 'Logga in' }}</button>
      <p v-if="store.version?.environment" class="hint small">
        {{ store.version.environment === 'Demo' ? 'Demo' : 'Testmiljö' }}: logga in med <code>admin</code> / <code>admin</code> eller <code>planerare</code> / <code>planerare</code>.
      </p>
    </form>
  </div>
</template>

<style scoped>
.login-page {
  min-height: calc(100dvh - var(--topbar-h) - var(--bottomnav-h));
  display: grid;
  place-items: center;
  padding: var(--sp-4);
}
.login {
  width: min(400px, 100%);
  padding: var(--sp-6) var(--sp-5);
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
}
.error {
  color: var(--danger);
  background: var(--danger-soft);
  padding: 8px 12px;
  border-radius: var(--r-md);
  font-size: var(--fs-sm);
}
.btn.primary {
  height: 40px;
}
.hint {
  padding: 8px 12px;
  border-radius: var(--r-md);
  background: var(--accent-soft);
  color: var(--text);
}
</style>
