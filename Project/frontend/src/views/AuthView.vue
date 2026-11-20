<template>
  <main class="container auth-page">
    <div class="breadcrumb">
      <RouterLink to="/">Каталог товарів</RouterLink> / Вхід та реєстрація
    </div>

    <div class="auth-card panel">
      <div class="auth-tabs" role="tablist">
        <button
          type="button"
          class="auth-tab"
          :class="{ active: mode === 'login' }"
          @click="switchMode('login')"
        >
          Вхід до кабінету
        </button>
        <button
          type="button"
          class="auth-tab"
          :class="{ active: mode === 'register' }"
          @click="switchMode('register')"
        >
          Реєстрація покупця
        </button>
      </div>

      <div v-if="reasonMessage" class="alert alert-info" role="status">
        {{ reasonMessage }}
      </div>

      <div v-if="errorMessage" class="alert alert-error" role="alert">
        {{ errorMessage }}
      </div>

      <form v-if="mode === 'login'" @submit.prevent="submitLogin" novalidate>
        <h1>Вхід до облікового запису</h1>
        <p class="auth-subtitle">
          Увійдіть, щоб зберігати товари в особистому кошику, оформлювати замовлення або керувати каталогом.
        </p>

        <div class="field">
          <label for="login-email">Електронна пошта</label>
          <input
            id="login-email"
            v-model.trim="loginForm.email"
            type="email"
            class="input"
            required
            autocomplete="email"
            placeholder="name@papir.local"
          />
        </div>

        <div class="field">
          <label for="login-password">Пароль</label>
          <input
            id="login-password"
            v-model="loginForm.password"
            type="password"
            class="input"
            required
            autocomplete="current-password"
            placeholder="Введіть пароль"
          />
        </div>

        <button type="submit" class="btn btn-primary auth-submit" :disabled="submitting">
          {{ submitting ? 'Перевірка даних…' : 'Увійти' }}
        </button>
      </form>

      <form v-else @submit.prevent="submitRegister" novalidate>
        <h1>Реєстрація покупця</h1>
        <p class="auth-subtitle">
          Створіть обліковий запис покупця для роботи з кошиком та оформлення замовлень.
        </p>

        <div class="form-row">
          <div class="field">
            <label for="reg-firstName">Ім’я *</label>
            <input
              id="reg-firstName"
              v-model.trim="registerForm.firstName"
              type="text"
              class="input"
              maxlength="100"
              required
              placeholder="Наприклад: Олена"
            />
          </div>
          <div class="field">
            <label for="reg-lastName">Прізвище *</label>
            <input
              id="reg-lastName"
              v-model.trim="registerForm.lastName"
              type="text"
              class="input"
              maxlength="100"
              required
              placeholder="Наприклад: Коваль"
            />
          </div>
        </div>

        <div class="field">
          <label for="reg-middleName">По батькові (необов’язково)</label>
          <input
            id="reg-middleName"
            v-model.trim="registerForm.middleName"
            type="text"
            class="input"
            maxlength="100"
            placeholder="Наприклад: Іванівна"
          />
        </div>

        <div class="field">
          <label for="reg-email">Електронна пошта *</label>
          <input
            id="reg-email"
            v-model.trim="registerForm.email"
            type="email"
            class="input"
            maxlength="255"
            required
            autocomplete="email"
            placeholder="buyer@example.com"
          />
        </div>

        <div class="field">
          <label for="reg-password">Пароль (щонайменше 6 символів) *</label>
          <input
            id="reg-password"
            v-model="registerForm.password"
            type="password"
            class="input"
            minlength="6"
            maxlength="100"
            required
            autocomplete="new-password"
            placeholder="Не менше 6 символів"
          />
        </div>

        <button type="submit" class="btn btn-primary auth-submit" :disabled="submitting">
          {{ submitting ? 'Створення акаунта…' : 'Зареєструватися' }}
        </button>
      </form>

      <div class="demo-accounts-box">
        <h2>Демонстраційні облікові записи для перевірки</h2>
        <p class="field-help">Натисніть на акаунт, щоб швидко підставити його дані у форму входу:</p>
        <div class="demo-buttons">
          <button type="button" class="btn btn-secondary btn-sm" @click="fillDemo('olena@papir.local', 'BuyerDemo!2026')">
            Покупець 1: Олена Коваль (olena@papir.local)
          </button>
          <button type="button" class="btn btn-secondary btn-sm" @click="fillDemo('andrii@papir.local', 'BuyerDemo!2026')">
            Покупець 2: Андрій Бондаренко (andrii@papir.local)
          </button>
          <button type="button" class="btn btn-outline btn-sm" @click="fillDemo('admin@papir.local', 'AdminDemo!2026')">
            Адміністратор: Ірина Куриленко (admin@papir.local)
          </button>
        </div>
      </div>
    </div>
  </main>
</template>

<script setup>
import { ref, computed } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useCartStore } from '../stores/cart'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const cartStore = useCartStore()

const mode = ref(route.query.mode === 'register' ? 'register' : 'login')
const submitting = ref(false)
const errorMessage = ref('')

const loginForm = ref({
  email: '',
  password: ''
})

const registerForm = ref({
  firstName: '',
  lastName: '',
  middleName: '',
  email: '',
  password: ''
})

const reasonMessage = computed(() => {
  if (route.query.reason === 'cart') {
    return 'Щоб додати товар до збереженого кошика та оформити замовлення, увійдіть до свого облікового запису або зареєструйтеся.'
  }
  if (route.query.reason === 'auth') {
    return 'Для перегляду цієї сторінки необхідно увійти до облікового запису.'
  }
  return ''
})

function switchMode(nextMode) {
  mode.value = nextMode
  errorMessage.value = ''
}

function fillDemo(email, password) {
  mode.value = 'login'
  errorMessage.value = ''
  loginForm.value.email = email
  loginForm.value.password = password
}

async function submitLogin() {
  errorMessage.value = ''
  if (!loginForm.value.email || !loginForm.value.password) {
    errorMessage.value = 'Вкажіть електронну пошту та пароль.'
    return
  }

  submitting.value = true
  try {
    await authStore.login(loginForm.value.email, loginForm.value.password)
    await cartStore.fetchCart()
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/')
      ? route.query.redirect
      : authStore.isAdmin ? '/admin/products' : '/'
    router.push(redirect)
  } catch (err) {
    errorMessage.value = err.message || 'Не вдалося виконати вхід.'
  } finally {
    submitting.value = false
  }
}

async function submitRegister() {
  errorMessage.value = ''
  if (!registerForm.value.firstName || !registerForm.value.lastName || !registerForm.value.email || !registerForm.value.password) {
    errorMessage.value = 'Заповніть усі обов’язкові поля реєстрації.'
    return
  }
  if (registerForm.value.password.length < 6) {
    errorMessage.value = 'Пароль повинен містити щонайменше 6 символів.'
    return
  }

  submitting.value = true
  try {
    await authStore.register({
      firstName: registerForm.value.firstName,
      lastName: registerForm.value.lastName,
      middleName: registerForm.value.middleName || null,
      email: registerForm.value.email,
      password: registerForm.value.password
    })
    await cartStore.fetchCart()
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/')
      ? route.query.redirect
      : '/'
    router.push(redirect)
  } catch (err) {
    errorMessage.value = err.message || 'Не вдалося зареєструвати обліковий запис.'
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.auth-page {
  padding-bottom: 48px;
}

.auth-card {
  max-width: 580px;
  margin: 0 auto;
}

.auth-tabs {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  background: var(--surface-alt);
  padding: 6px;
  border-radius: var(--radius-sm);
  margin-bottom: 24px;
}

.auth-tab {
  border: 0;
  background: transparent;
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  font-weight: 600;
  color: var(--text-muted);
  font-size: 14px;
}

.auth-tab.active {
  background: var(--surface);
  color: var(--burgundy);
  box-shadow: var(--shadow-sm);
}

h1 {
  margin: 0 0 8px;
  font-size: 26px;
}

.auth-subtitle {
  margin: 0 0 22px;
  color: var(--text-muted);
  font-size: 14px;
  line-height: 1.5;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
}

.auth-submit {
  width: 100%;
  padding: 13px;
  margin-top: 6px;
}

.demo-accounts-box {
  margin-top: 28px;
  padding-top: 20px;
  border-top: 1px solid var(--border-light);
}

.demo-accounts-box h2 {
  font-size: 15px;
  margin: 0 0 4px;
}

.demo-buttons {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
}

.btn-sm {
  font-size: 13px;
  padding: 9px 12px;
  justify-content: flex-start;
  white-space: normal;
  text-align: left;
}

@media (max-width: 520px) {
  .form-row {
    grid-template-columns: 1fr;
    gap: 0;
  }
}
</style>
