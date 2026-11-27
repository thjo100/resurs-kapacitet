import { createApp } from 'vue'
import App from './App.vue'
import { router } from './router.js'
import './styles/tokens.css'
import './styles/base.css'
import './styles/grid.css'

if (import.meta.env.VITE_DEMO === 'true') {
  // I demoläget finns ingen server – export och säkerhetskopia fungerar bara i den riktiga installationen.
  document.addEventListener('click', async (e) => {
    const a = e.target.closest?.('a[href^="/api/"]')
    if (!a) return
    e.preventDefault()
    const { toast } = await import('./store.js')
    toast('Export och säkerhetskopia fungerar i den riktiga installationen, inte i demoläget.')
  })
}

createApp(App).use(router).mount('#app')
