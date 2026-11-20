import { defineStore } from 'pinia'
import { ref } from 'vue'
import { apiRequest } from '../services/api'
import { useAuthStore } from './auth'

export const useCartStore = defineStore('cart', () => {
  const items = ref([])
  const totalQuantity = ref(0)
  const totalAmount = ref(0)
  const canCheckout = ref(false)
  const cartSnapshotToken = ref('')
  const warnings = ref([])
  const loading = ref(false)

  function applyCartResponse(data) {
    items.value = data?.items || []
    totalQuantity.value = data?.totalQuantity || 0
    totalAmount.value = data?.totalAmount || 0
    canCheckout.value = Boolean(data?.canCheckout)
    cartSnapshotToken.value = data?.cartSnapshotToken || ''
    warnings.value = data?.warnings || []
  }

  function resetCartState() {
    items.value = []
    totalQuantity.value = 0
    totalAmount.value = 0
    canCheckout.value = false
    cartSnapshotToken.value = ''
    warnings.value = []
  }

  async function fetchCart() {
    const auth = useAuthStore()
    if (!auth.isAuthenticated) {
      resetCartState()
      return
    }
    loading.value = true
    try {
      const data = await apiRequest('/api/cart')
      applyCartResponse(data)
    } catch {
      resetCartState()
    } finally {
      loading.value = false
    }
  }

  async function addItem(productId, quantity = 1) {
    const data = await apiRequest('/api/cart/items', {
      method: 'POST',
      body: JSON.stringify({ productId, quantity })
    })
    applyCartResponse(data)
    return data
  }

  async function updateQuantity(productId, quantity) {
    const data = await apiRequest(`/api/cart/items/${productId}`, {
      method: 'PUT',
      body: JSON.stringify({ quantity })
    })
    applyCartResponse(data)
    return data
  }

  async function removeItem(productId) {
    const data = await apiRequest(`/api/cart/items/${productId}`, {
      method: 'DELETE'
    })
    applyCartResponse(data)
    return data
  }

  return {
    items,
    totalQuantity,
    totalAmount,
    canCheckout,
    cartSnapshotToken,
    warnings,
    loading,
    applyCartResponse,
    resetCartState,
    fetchCart,
    addItem,
    updateQuantity,
    removeItem
  }
})
