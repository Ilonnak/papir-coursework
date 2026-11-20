<template>
  <main class="container product-page">
    <div class="breadcrumb">
      <RouterLink to="/">Каталог товарів</RouterLink>
      <template v-if="product">
        / {{ product.categoryName }} / {{ product.name }}
      </template>
      <template v-else> / Перегляд товару</template>
    </div>

    <div v-if="loading" class="panel state-panel" role="status">
      Завантаження відомостей про товар…
    </div>

    <div v-else-if="notFound || !product" class="panel state-panel">
      <h1>Товар недоступний</h1>
      <p class="field-help">
        Обраний товар не знайдено або його було приховано з публічного каталогу.
      </p>
      <RouterLink to="/" class="btn btn-primary" style="margin-top: 14px">
        ← Повернутися до каталогу
      </RouterLink>
    </div>

    <template v-else>
      <div class="product-top">
        <div class="product-picture panel">
          <img
            :src="resolveImageUrl(product.imagePath)"
            :alt="product.name"
            @error="onImgError"
          />
        </div>

        <section class="product-main panel">
          <div class="category-label">{{ product.categoryName }}</div>
          <h1>{{ product.name }}</h1>

          <div
            class="stock-line"
            :class="product.stockQuantity > 0 ? 'in-stock' : 'out-of-stock'"
          >
            <span v-if="product.stockQuantity > 0">
              ● Є в наявності · Доступно {{ product.stockQuantity }} шт.
            </span>
            <span v-else>
              ✕ Немає в наявності (0 шт. на складі)
            </span>
          </div>

          <p class="product-summary">{{ product.description }}</p>

          <div class="price-display">{{ formatMoney(product.price) }}</div>

          <div class="purchase-box">
            <div>
              <label for="qty-input">Кількість, шт.</label>
              <div class="qty-control">
                <button
                  type="button"
                  aria-label="Зменшити кількість"
                  :disabled="product.stockQuantity <= 0 || quantity <= 1"
                  @click="changeQty(-1)"
                >
                  −
                </button>
                <input
                  id="qty-input"
                  v-model.number="quantity"
                  type="number"
                  min="1"
                  :max="Math.max(1, product.stockQuantity)"
                  step="1"
                  :disabled="product.stockQuantity <= 0"
                />
                <button
                  type="button"
                  aria-label="Збільшити кількість"
                  :disabled="product.stockQuantity <= 0 || quantity >= product.stockQuantity"
                  @click="changeQty(1)"
                >
                  +
                </button>
              </div>
            </div>

            <button
              type="button"
              class="btn btn-primary buy-btn"
              :disabled="product.stockQuantity <= 0 || !isQtyValid || submitting"
              @click="addToCart"
            >
              <template v-if="product.stockQuantity <= 0">Немає в наявності</template>
              <template v-else-if="submitting">Додавання…</template>
              <template v-else>Додати до кошика</template>
            </button>
          </div>

          <div v-if="product.stockQuantity > 0" class="total-line">
            Разом за обрану кількість:
            <strong>{{ isQtyValid ? formatMoney(product.price * quantity) : '—' }}</strong>
          </div>

          <p v-if="!isQtyValid && product.stockQuantity > 0" class="field-error" role="alert">
            Вкажіть цілу кількість від 1 до {{ product.stockQuantity }} шт.
          </p>

          <div v-if="guestPrompt" class="alert alert-info" style="margin-top: 16px">
            <p style="margin: 0 0 10px">
              Щоб додати цей товар до особистого кошика, увійдіть до свого облікового запису покупця.
            </p>
            <RouterLink
              :to="{ path: '/auth', query: { reason: 'cart', redirect: `/products/${product.id}` } }"
              class="btn btn-primary"
            >
              Увійти або зареєструватися
            </RouterLink>
          </div>

          <div v-if="feedbackMessage" class="alert alert-success" role="status">
            {{ feedbackMessage }}
            <div style="margin-top: 8px">
              <RouterLink to="/cart" class="btn btn-secondary" style="padding: 6px 12px; font-size: 13px">
                Перейти до кошика →
              </RouterLink>
            </div>
          </div>

          <div v-if="errorMessage" class="alert alert-error" role="alert">
            {{ errorMessage }}
          </div>
        </section>
      </div>

      <div class="product-details">
        <section class="panel">
          <h2>Опис товару</h2>
          <p class="detail-text">{{ product.description }}</p>
        </section>

        <section class="panel">
          <h2>Характеристики</h2>
          <table class="specs-table">
            <tbody>
              <tr>
                <th scope="row">Категорія</th>
                <td>{{ product.categoryName }}</td>
              </tr>
              <tr v-for="(spec, idx) in parsedSpecs" :key="idx">
                <th scope="row">{{ spec.key }}</th>
                <td>{{ spec.value }}</td>
              </tr>
            </tbody>
          </table>
        </section>
      </div>
    </template>
  </main>
</template>

<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { apiRequest, resolveImageUrl } from '../services/api'
import { formatMoney } from '../utils/formatters'
import { useAuthStore } from '../stores/auth'
import { useCartStore } from '../stores/cart'

const route = useRoute()
const authStore = useAuthStore()
const cartStore = useCartStore()

const product = ref(null)
const loading = ref(true)
const notFound = ref(false)
const quantity = ref(1)
const submitting = ref(false)
const feedbackMessage = ref('')
const errorMessage = ref('')
const guestPrompt = ref(false)

const isQtyValid = computed(() => {
  if (!product.value || product.value.stockQuantity <= 0) return false
  const q = Number(quantity.value)
  return Number.isInteger(q) && q >= 1 && q <= product.value.stockQuantity
})

const parsedSpecs = computed(() => {
  if (!product.value?.specifications) return []
  return product.value.specifications
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean)
    .map(line => {
      const colonIdx = line.indexOf(':')
      if (colonIdx > 0) {
        return {
          key: line.slice(0, colonIdx).trim(),
          value: line.slice(colonIdx + 1).trim()
        }
      }
      return { key: 'Параметр', value: line }
    })
})

function changeQty(delta) {
  if (!product.value) return
  const next = (Number(quantity.value) || 1) + delta
  quantity.value = Math.max(1, Math.min(product.value.stockQuantity, next))
  feedbackMessage.value = ''
  errorMessage.value = ''
}

async function fetchProduct() {
  loading.value = true
  notFound.value = false
  errorMessage.value = ''
  feedbackMessage.value = ''
  guestPrompt.value = false

  try {
    const id = Number(route.params.id)
    if (!Number.isInteger(id) || id <= 0) {
      notFound.value = true
      return
    }
    product.value = await apiRequest(`/api/products/${id}`)
    quantity.value = 1
  } catch (err) {
    if (err.status === 404) {
      notFound.value = true
    } else {
      errorMessage.value = err.message || 'Помилка завантаження сторінки товару.'
    }
  } finally {
    loading.value = false
  }
}

async function addToCart() {
  feedbackMessage.value = ''
  errorMessage.value = ''

  if (!authStore.isAuthenticated) {
    guestPrompt.value = true
    return
  }

  if (!isQtyValid.value) return

  submitting.value = true
  try {
    await cartStore.addItem(product.value.id, Number(quantity.value))
    feedbackMessage.value = `Додано до кошика: ${quantity.value} шт. «${product.value.name}».`
  } catch (err) {
    errorMessage.value = err.message || 'Не вдалося додати товар до кошика.'
  } finally {
    submitting.value = false
  }
}

function onImgError(e) {
  e.target.src = resolveImageUrl('/images/products/placeholder.svg')
}

watch(() => route.params.id, fetchProduct)
onMounted(fetchProduct)
</script>

<style scoped>
.product-page {
  padding-bottom: 50px;
}

.state-panel {
  text-align: center;
  padding: 48px 24px;
}

.product-top {
  display: grid;
  grid-template-columns: 1fr 1.15fr;
  gap: 32px;
  align-items: stretch;
}

.product-picture {
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--surface);
  min-height: 360px;
  padding: 20px;
}

.product-picture img {
  max-width: 100%;
  max-height: 360px;
  width: auto;
  height: auto;
  object-fit: contain;
  object-position: center;
  border-radius: var(--radius-sm);
  display: block;
}

.category-label {
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 1px;
  color: var(--burgundy);
  font-weight: 700;
}

h1 {
  font-size: 28px;
  line-height: 1.3;
  margin: 10px 0 14px;
}

.stock-line {
  font-size: 14px;
  font-weight: 600;
  margin-bottom: 16px;
}

.stock-line.in-stock {
  color: var(--success-text);
}

.stock-line.out-of-stock {
  color: var(--danger-text);
}

.product-summary {
  color: var(--text-muted);
  line-height: 1.65;
  margin-bottom: 20px;
}

.price-display {
  font-size: 32px;
  font-weight: 800;
  color: var(--text-main);
  margin: 18px 0;
}

.purchase-box {
  display: flex;
  align-items: flex-end;
  gap: 16px;
  flex-wrap: wrap;
  padding: 20px 0;
  border-top: 1px solid var(--border-light);
}

.purchase-box label {
  display: block;
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 8px;
}

.qty-control {
  display: flex;
  border: 1px solid #D8CBC5;
  border-radius: var(--radius-sm);
  background: var(--surface);
  overflow: hidden;
}

.qty-control button {
  border: 0;
  background: var(--burgundy-soft);
  color: var(--burgundy-dark);
  width: 40px;
  padding: 10px 0;
  font-weight: 700;
}

.qty-control button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.qty-control input {
  width: 60px;
  min-width: 0;
  border: 0;
  text-align: center;
  font-weight: 600;
}

.buy-btn {
  padding: 12px 26px;
}

.total-line {
  font-size: 14px;
  color: var(--text-muted);
}

.product-details {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 28px;
  margin-top: 28px;
}

.product-details h2 {
  margin: 0 0 16px;
  font-size: 20px;
}

.detail-text {
  margin: 0;
  color: var(--text-muted);
  line-height: 1.75;
  white-space: pre-line;
}

.specs-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}

.specs-table th,
.specs-table td {
  padding: 11px 8px;
  border-bottom: 1px solid #EFE8E4;
  text-align: left;
  vertical-align: top;
}

.specs-table th {
  font-weight: 600;
  color: var(--text-muted);
  width: 46%;
}

@media (max-width: 780px) {
  .product-top,
  .product-details {
    grid-template-columns: 1fr;
    gap: 20px;
  }
  .product-picture {
    min-height: 240px;
  }
  h1 {
    font-size: 23px;
  }
}
</style>
