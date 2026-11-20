import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { apiRequest } from '../services/api'

export const useAuthStore = defineStore('auth', () => {
  const token = ref(localStorage.getItem('papir_jwt') || '')
  const expiresAtUtc = ref(localStorage.getItem('papir_jwt_exp') || '')
  const user = ref(loadStoredUser())
  const initialized = ref(false)

  function loadStoredUser() {
    try {
      const raw = localStorage.getItem('papir_user')
      return raw ? JSON.parse(raw) : null
    } catch {
      return null
    }
  }

  const isAuthenticated = computed(() => {
    if (!token.value || !user.value) return false
    if (expiresAtUtc.value) {
      const expTime = Date.parse(expiresAtUtc.value)
      if (!Number.isNaN(expTime) && expTime <= Date.now()) {
        return false
      }
    }
    return true
  })

  const isAdmin = computed(() => isAuthenticated.value && Boolean(user.value?.isAdmin))
  const isBuyer = computed(() => isAuthenticated.value && !user.value?.isAdmin)

  const fullName = computed(() => {
    if (!user.value) return ''
    return `${user.value.firstName || ''} ${user.value.lastName || ''}`.trim()
  })

  function setSession(authResponse) {
    token.value = authResponse.token
    expiresAtUtc.value = authResponse.expiresAtUtc
    user.value = authResponse.user
    localStorage.setItem('papir_jwt', authResponse.token)
    localStorage.setItem('papir_jwt_exp', authResponse.expiresAtUtc)
    localStorage.setItem('papir_user', JSON.stringify(authResponse.user))
  }

  function clearSession() {
    token.value = ''
    expiresAtUtc.value = ''
    user.value = null
    localStorage.removeItem('papir_jwt')
    localStorage.removeItem('papir_jwt_exp')
    localStorage.removeItem('papir_user')
  }

  async function verifySession() {
    if (!token.value) {
      clearSession()
      initialized.value = true
      return false
    }

    if (expiresAtUtc.value && Date.parse(expiresAtUtc.value) <= Date.now()) {
      clearSession()
      initialized.value = true
      return false
    }

    try {
      const profile = await apiRequest('/api/auth/me')
      user.value = profile
      localStorage.setItem('papir_user', JSON.stringify(profile))
      initialized.value = true
      return true
    } catch {
      clearSession()
      initialized.value = true
      return false
    }
  }

  async function login(email, password) {
    const res = await apiRequest('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password })
    })
    setSession(res)
    return res
  }

  async function register(payload) {
    const res = await apiRequest('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify(payload)
    })
    setSession(res)
    return res
  }

  async function logout() {
    try {
      if (token.value) {
        await apiRequest('/api/auth/logout', { method: 'POST' })
      }
    } catch {
      // Ігноруємо помилку мережі при виході — локальний стан все одно очищується
    } finally {
      clearSession()
    }
  }

  return {
    token,
    expiresAtUtc,
    user,
    initialized,
    isAuthenticated,
    isAdmin,
    isBuyer,
    fullName,
    setSession,
    clearSession,
    verifySession,
    login,
    register,
    logout
  }
})
