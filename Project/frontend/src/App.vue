<template>
  <header class="site-header">
    <div class="container header-top">
      <RouterLink to="/" class="brand-logo">папір<span>.</span></RouterLink>
      <div class="header-tagline">Канцелярія для навчання, роботи та натхнення</div>

      <div class="header-actions">
        <template v-if="authStore.isAuthenticated">
          <span class="user-pill">
            {{ authStore.isAdmin ? 'Адміністратор:' : 'Покупець:' }}
            {{ authStore.fullName }}
          </span>
          <RouterLink to="/cart" class="btn btn-secondary">
            Кошик · <strong>{{ cartStore.totalQuantity }}</strong>
          </RouterLink>
          <button type="button" class="btn btn-outline" @click="handleLogout">
            Вийти
          </button>
        </template>
        <template v-else>
          <RouterLink to="/auth" class="btn btn-primary">
            Увійти / Реєстрація
          </RouterLink>
        </template>
      </div>
    </div>

    <nav class="site-nav" aria-label="Основна навігація">
      <div class="container nav-inner">
        <RouterLink to="/" class="nav-link">Каталог товарів</RouterLink>

        <template v-if="authStore.isAuthenticated">
          <RouterLink to="/orders" class="nav-link">Мої замовлення</RouterLink>
          <RouterLink to="/cart" class="nav-link">
            Кошик ({{ cartStore.totalQuantity }})
          </RouterLink>
        </template>

        <template v-if="authStore.isAdmin">
          <RouterLink to="/admin/products" class="nav-link">
            Адмін: Товарні пропозиції
          </RouterLink>
          <RouterLink to="/admin/orders" class="nav-link">
            Адмін: Замовлення покупців
          </RouterLink>
        </template>
      </div>
    </nav>
  </header>

  <RouterView />

  <footer class="site-footer">
    <div class="container footer-inner">
      <div><strong>папір.</strong> · Каталог канцелярських товарів для навчання та роботи</div>
      <div>Навчальний застосунок курсової роботи · Самовивіз та оплата при отриманні</div>
    </div>
  </footer>
</template>

<script setup>
import { onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from './stores/auth'
import { useCartStore } from './stores/cart'

const router = useRouter()
const authStore = useAuthStore()
const cartStore = useCartStore()

async function handleLogout() {
  await authStore.logout()
  cartStore.resetCartState()
  router.push('/')
}

function onUnauthorizedEvent() {
  authStore.clearSession()
  cartStore.resetCartState()
}

onMounted(async () => {
  window.addEventListener('papir:unauthorized', onUnauthorizedEvent)
  const valid = await authStore.verifySession()
  if (valid) {
    await cartStore.fetchCart()
  }
})

onUnmounted(() => {
  window.removeEventListener('papir:unauthorized', onUnauthorizedEvent)
})
</script>
