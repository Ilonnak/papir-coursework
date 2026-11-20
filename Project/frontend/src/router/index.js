import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import CatalogView from '../views/CatalogView.vue'
import ProductView from '../views/ProductView.vue'
import CartView from '../views/CartView.vue'
import OrdersView from '../views/OrdersView.vue'
import AdminProductsView from '../views/AdminProductsView.vue'
import AdminOrdersView from '../views/AdminOrdersView.vue'
import AuthView from '../views/AuthView.vue'

const routes = [
  {
    path: '/',
    name: 'catalog',
    component: CatalogView
  },
  {
    path: '/products/:id',
    name: 'product',
    component: ProductView
  },
  {
    path: '/auth',
    name: 'auth',
    component: AuthView
  },
  {
    path: '/cart',
    name: 'cart',
    component: CartView,
    meta: { requiresAuth: true }
  },
  {
    path: '/orders',
    name: 'orders',
    component: OrdersView,
    meta: { requiresAuth: true }
  },
  {
    path: '/admin/products',
    name: 'admin-products',
    component: AdminProductsView,
    meta: { requiresAuth: true, requiresAdmin: true }
  },
  {
    path: '/admin/orders',
    name: 'admin-orders',
    component: AdminOrdersView,
    meta: { requiresAuth: true, requiresAdmin: true }
  },
  {
    path: '/:pathMatch(.*)*',
    redirect: '/'
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (!auth.initialized) {
    await auth.verifySession()
  }

  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return {
      path: '/auth',
      query: { reason: 'auth', redirect: to.fullPath }
    }
  }

  if (to.meta.requiresAdmin && !auth.isAdmin) {
    return { path: '/' }
  }

  return true
})

export default router
