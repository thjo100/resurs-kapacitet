import { createRouter, createWebHistory } from 'vue-router'
import { store, loadMe } from './store.js'

const routes = [
  { path: '/', name: 'overview', component: () => import('./views/OverviewView.vue'), meta: { title: 'Översikt' } },
  { path: '/planering', name: 'planning', component: () => import('./views/PlanningView.vue'), meta: { title: 'Planering' } },
  { path: '/backlogg', name: 'backlog', component: () => import('./views/BacklogView.vue'), meta: { title: 'Backlogg' } },
  { path: '/uppdrag', name: 'assignments', component: () => import('./views/AssignmentsView.vue'), meta: { title: 'Uppdrag' } },
  { path: '/andringar', name: 'changes', component: () => import('./views/ChangesView.vue'), meta: { title: 'Ändringslogg' } },
  { path: '/nyheter', name: 'news', component: () => import('./views/NewsView.vue'), meta: { title: 'Nyheter' } },
  { path: '/logga-in', name: 'login', component: () => import('./views/LoginView.vue'), meta: { title: 'Logga in' } },
  {
    path: '/admin/:tab?',
    name: 'admin',
    component: () => import('./views/AdminView.vue'),
    meta: { title: 'Admin', requiresRole: ['Admin', 'Planerare'] },
  },
  { path: '/:pathMatch(.*)*', redirect: '/' },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
})

router.beforeEach(async (to) => {
  if (!store.meLoaded) await loadMe()
  if (to.meta.requiresRole && !to.meta.requiresRole.includes(store.me?.role)) {
    return { name: 'login', query: { till: to.fullPath } }
  }
})

router.afterEach((to) => {
  document.title = `${to.meta.title ?? ''} · Resurs & Kapacitet`
})
