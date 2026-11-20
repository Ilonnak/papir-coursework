<template>
  <main class="container catalog-page">
    <div class="breadcrumb">Головна / Каталог товарів</div>

    <section class="intro-banner">
      <div class="intro-text">
        <h1>Для великих ідей<br />і щоденних нотаток</h1>
        <p>Канцелярія для навчання, роботи та натхнення. Упорядкований каталог із актуальними залишками.</p>
      </div>
      <div class="intro-mark" aria-hidden="true">Aa.</div>
    </section>

    <div v-if="guestPromptVisible" class="alert alert-info guest-banner" role="alert">
      <div>
        <strong>Потрібен вхід до облікового запису:</strong>
        щоб додавати товари до особистого кошика та оформлювати замовлення, увійдіть або зареєструйтеся.
      </div>
      <div class="guest-banner-actions">
        <RouterLink :to="{ path: '/auth', query: { reason: 'cart', redirect: '/' } }" class="btn btn-primary btn-xs">
          Увійти / Реєстрація
        </RouterLink>
        <button type="button" class="btn btn-outline btn-xs" @click="guestPromptVisible = false">
          Закрити
        </button>
      </div>
    </div>

    <div class="catalog-layout">
      <aside class="filters-panel panel" aria-label="Фільтри каталогу">
        <div class="filter-block">
          <label for="catalog-search" class="filter-heading">Пошук за назвою</label>
          <input
            id="catalog-search"
            v-model.trim="searchInput"
            type="search"
            class="input"
            placeholder="Блокнот, ручка, папка…"
            aria-label="Пошук товарів за назвою"
            @keyup.enter="applyFilters"
          />
        </div>

        <div class="filter-block">
          <h2 class="filter-heading">Категорії</h2>
          <button
            type="button"
            class="category-btn"
            :class="{ active: selectedCategoryId === null }"
            @click="selectCategory(null)"
          >
            <span>Усі товари</span>
          </button>
          <button
            v-for="cat in categories"
            :key="cat.id"
            type="button"
            class="category-btn"
            :class="{ active: selectedCategoryId === cat.id }"
            @click="selectCategory(cat.id)"
          >
            <span>{{ cat.name }}</span>
            <small class="cat-count">{{ cat.activeProductCount }}</small>
          </button>
        </div>

        <div class="filter-block filter-divider">
          <h2 class="filter-heading">Ціна, грн</h2>
          <div class="price-fields">
            <div>
              <label for="min-price" class="sr-only">Ціна від</label>
              <input
                id="min-price"
                v-model="minPriceInput"
                type="number"
                min="0"
                step="1"
                class="input"
                placeholder="Від"
                aria-label="Ціна від"
              />
            </div>
            <div>
              <label for="max-price" class="sr-only">Ціна до</label>
              <input
                id="max-price"
                v-model="maxPriceInput"
                type="number"
                min="0"
                step="1"
                class="input"
                placeholder="До"
                aria-label="Ціна до"
              />
            </div>
          </div>
          <p v-if="filterError" class="field-error" role="alert">{{ filterError }}</p>
          <button type="button" class="btn btn-primary full-width" @click="applyFilters">
            Застосувати фільтри
          </button>
          <button type="button" class="btn-reset" @click="resetFilters">
            Скинути всі фільтри
          </button>
        </div>
      </aside>

      <section class="products-section" aria-label="Товари каталогу">
        <div class="catalog-toolbar panel">
          <div class="toolbar-left">
            <h2 class="toolbar-title">Каталог товарів</h2>
            <span class="result-count" aria-live="polite">
              Знайдено: <strong>{{ totalCount }}</strong>
            </span>
          </div>

          <div class="toolbar-right">
            <label for="sort-select" class="sort-label">Сортування:</label>
            <select
              id="sort-select"
              v-model="sortBy"
              class="select sort-select"
              aria-label="Сортування товарів"
              @change="onSortChange"
            >
              <option value="name">За назвою (А → Я)</option>
              <option value="name_desc">За назвою (Я → А)</option>
              <option value="price_asc">Спочатку дешевші</option>
              <option value="price_desc">Спочатку дорожчі</option>
            </select>
          </div>
        </div>

        <div v-if="serverError" class="alert alert-error" role="alert">
          {{ serverError }}
        </div>

        <div v-if="actionFeedback" class="alert alert-success" role="status">
          {{ actionFeedback }}
        </div>

        <div v-if="loading" class="state-box panel" role="status">
          Завантаження товарів каталогу…
        </div>

        <div v-else-if="products.length === 0 && !serverError" class="state-box panel">
          <p><strong>Товарів за обраними умовами не знайдено.</strong></p>
          <p class="field-help">Спробуйте змінити пошуковий запит, діапазон ціни або скинути фільтри.</p>
          <button type="button" class="btn btn-secondary" style="margin-top: 10px" @click="resetFilters">
            Показати всі товари
          </button>
        </div>

        <div v-else class="products-grid">
          <article v-for="item in products" :key="item.id" class="product-card panel">
            <RouterLink :to="`/products/${item.id}`" class="card-image-link">
              <div class="card-picture">
                <img
                  :src="resolveImageUrl(item.imagePath)"
                  :alt="item.name"
                  loading="lazy"
                  @error="onImgError"
                />
              </div>
            </RouterLink>

            <div class="card-category">{{ item.categoryName }}</div>

            <h3 class="card-title">
              <RouterLink :to="`/products/${item.id}`">{{ item.name }}</RouterLink>
            </h3>

            <div class="card-stock" :class="item.stockQuantity > 0 ? 'in-stock' : 'out-of-stock'">
              <span v-if="item.stockQuantity > 0">● Є в наявності · {{ item.stockQuantity }} шт.</span>
              <span v-else>✕ Немає в наявності (0 шт.)</span>
            </div>

            <div class="card-bottom">
              <span class="card-price">{{ formatMoney(item.price) }}</span>
              <button
                type="button"
                class="btn"
                :class="addedProductIds[item.id] ? 'btn-secondary' : 'btn-primary'"
                :disabled="item.stockQuantity <= 0 || addingId === item.id"
                @click="handleAddToCart(item)"
              >
                <template v-if="item.stockQuantity <= 0">Недоступно</template>
                <template v-else-if="addingId === item.id">Додавання…</template>
                <template v-else-if="addedProductIds[item.id]">Додано ✓</template>
                <template v-else>До кошика</template>
              </button>
            </div>
          </article>
        </div>

        <nav
          v-if="totalPages > 1 && !loading"
          class="pagination"
          aria-label="Сторінки каталогу"
        >
          <button
            type="button"
            class="btn btn-outline page-btn"
            :disabled="currentPage <= 1"
            @click="goToPage(currentPage - 1)"
          >
            ← Попередня
          </button>

          <button
            v-for="p in totalPages"
            :key="p"
            type="button"
            class="btn page-num"
            :class="p === currentPage ? 'btn-primary' : 'btn-outline'"
            @click="goToPage(p)"
          >
            {{ p }}
          </button>

          <button
            type="button"
            class="btn btn-outline page-btn"
            :disabled="currentPage >= totalPages"
            @click="goToPage(currentPage + 1)"
          >
            Наступна →
          </button>
        </nav>
      </section>
    </div>
  </main>
</template>

<script setup>
import { ref, onMounted, reactive } from 'vue'
import { apiRequest, resolveImageUrl } from '../services/api'
import { formatMoney } from '../utils/formatters'
import { useAuthStore } from '../stores/auth'
import { useCartStore } from '../stores/cart'

const authStore = useAuthStore()
const cartStore = useCartStore()

const categories = ref([])
const products = ref([])
const totalCount = ref(0)
const totalPages = ref(1)
const currentPage = ref(1)
const pageSize = 9

const searchInput = ref('')
const selectedCategoryId = ref(null)
const minPriceInput = ref('')
const maxPriceInput = ref('')
const sortBy = ref('name')

const loading = ref(false)
const filterError = ref('')
const serverError = ref('')
const actionFeedback = ref('')
const guestPromptVisible = ref(false)
const addingId = ref(null)
const addedProductIds = reactive({})

async function loadCategories() {
  try {
    categories.value = await apiRequest('/api/categories')
  } catch {
    // ігноруємо, основна помилка покажеться під час завантаження товарів
  }
}

function validatePriceFilter() {
  filterError.value = ''
  const minVal = minPriceInput.value === '' || minPriceInput.value === null ? null : Number(minPriceInput.value)
  const maxVal = maxPriceInput.value === '' || maxPriceInput.value === null ? null : Number(maxPriceInput.value)

  if (minVal !== null && (Number.isNaN(minVal) || minVal < 0)) {
    filterError.value = 'Мінімальна ціна не може бути від’ємною.'
    return null
  }
  if (maxVal !== null && (Number.isNaN(maxVal) || maxVal < 0)) {
    filterError.value = 'Максимальна ціна не може бути від’ємною.'
    return null
  }
  if (minVal !== null && maxVal !== null && minVal > maxVal) {
    filterError.value = 'Значення «Від» не може перевищувати «До».'
    return null
  }
  return { minVal, maxVal }
}

async function loadProducts() {
  const priceRange = validatePriceFilter()
  if (!priceRange) return

  loading.value = true
  serverError.value = ''
  try {
    const params = new URLSearchParams()
    if (searchInput.value) params.set('search', searchInput.value)
    if (selectedCategoryId.value) params.set('categoryId', String(selectedCategoryId.value))
    if (priceRange.minVal !== null) params.set('minPrice', String(priceRange.minVal))
    if (priceRange.maxVal !== null) params.set('maxPrice', String(priceRange.maxVal))
    params.set('sort', sortBy.value)
    params.set('page', String(currentPage.value))
    params.set('pageSize', String(pageSize))

    const res = await apiRequest(`/api/products?${params.toString()}`)
    products.value = res.items || []
    totalCount.value = res.totalCount || 0
    totalPages.value = res.totalPages || 1
    currentPage.value = res.page || 1
  } catch (err) {
    serverError.value = err.message || 'Не вдалося завантажити каталог товарів.'
  } finally {
    loading.value = false
  }
}

function selectCategory(catId) {
  selectedCategoryId.value = catId
  currentPage.value = 1
  loadProducts()
}

function applyFilters() {
  currentPage.value = 1
  loadProducts()
}

function onSortChange() {
  currentPage.value = 1
  loadProducts()
}

function resetFilters() {
  searchInput.value = ''
  selectedCategoryId.value = null
  minPriceInput.value = ''
  maxPriceInput.value = ''
  sortBy.value = 'name'
  filterError.value = ''
  currentPage.value = 1
  loadProducts()
}

function goToPage(page) {
  if (page < 1 || page > totalPages.value) return
  currentPage.value = page
  loadProducts()
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

async function handleAddToCart(product) {
  actionFeedback.value = ''
  serverError.value = ''

  if (!authStore.isAuthenticated) {
    guestPromptVisible.value = true
    window.scrollTo({ top: 0, behavior: 'smooth' })
    return
  }

  addingId.value = product.id
  try {
    await cartStore.addItem(product.id, 1)
    addedProductIds[product.id] = true
    actionFeedback.value = `Товар «${product.name}» додано до кошика.`
    setTimeout(() => {
      addedProductIds[product.id] = false
    }, 2000)
  } catch (err) {
    serverError.value = err.message || 'Не вдалося додати товар до кошика.'
  } finally {
    addingId.value = null
  }
}

function onImgError(e) {
  e.target.src = resolveImageUrl('/images/products/placeholder.svg')
}

onMounted(async () => {
  await Promise.all([loadCategories(), loadProducts()])
})
</script>

<style scoped>
.catalog-page {
  padding-bottom: 50px;
}

.intro-banner {
  padding: 28px 34px;
  border-radius: var(--radius-lg);
  background: var(--burgundy-soft);
  border: 1px solid var(--burgundy-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  margin-bottom: 24px;
}

.intro-text h1 {
  margin: 0 0 10px;
  font-size: 32px;
  line-height: 1.2;
  color: var(--burgundy-dark);
}

.intro-text p {
  margin: 0;
  color: var(--text-muted);
  line-height: 1.5;
}

.intro-mark {
  font-size: 64px;
  font-weight: 800;
  color: #D9BDC6;
  letter-spacing: -4px;
  user-select: none;
}

.guest-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.guest-banner-actions {
  display: flex;
  gap: 8px;
}

.btn-xs {
  padding: 7px 12px;
  font-size: 13px;
}

.catalog-layout {
  display: grid;
  grid-template-columns: 255px 1fr;
  gap: 26px;
  align-items: start;
}

.filter-block {
  margin-bottom: 20px;
}

.filter-divider {
  padding-top: 20px;
  border-top: 1px solid var(--border-light);
  margin-bottom: 0;
}

.filter-heading {
  display: block;
  font-size: 15px;
  font-weight: 700;
  margin: 0 0 12px;
  color: var(--text-main);
}

.category-btn {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  text-align: left;
  border: 0;
  border-radius: var(--radius-sm);
  padding: 10px 12px;
  margin-bottom: 4px;
  background: transparent;
  color: var(--text-muted);
  font-size: 14px;
  transition: background-color 0.15s ease, color 0.15s ease;
}

.category-btn:hover {
  background: var(--surface-alt);
  color: var(--text-main);
}

.category-btn.active {
  background: var(--burgundy-soft);
  color: var(--burgundy-dark);
  font-weight: 700;
}

.cat-count {
  font-size: 12px;
  padding: 2px 7px;
  border-radius: 10px;
  background: rgba(109, 32, 56, 0.1);
  color: var(--burgundy-dark);
}

.price-fields {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  margin-bottom: 12px;
}

.full-width {
  width: 100%;
}

.btn-reset {
  border: 0;
  background: transparent;
  color: var(--text-muted);
  width: 100%;
  margin-top: 12px;
  font-size: 13px;
  padding: 6px;
  text-decoration: underline;
}

.btn-reset:hover {
  color: var(--burgundy);
}

.catalog-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 20px;
  margin-bottom: 20px;
  flex-wrap: wrap;
}

.toolbar-left {
  display: flex;
  align-items: baseline;
  gap: 16px;
  flex-wrap: wrap;
}

.toolbar-title {
  margin: 0;
  font-size: 20px;
}

.result-count {
  font-size: 14px;
  color: var(--text-muted);
}

.toolbar-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.sort-label {
  font-size: 13px;
  color: var(--text-muted);
  white-space: nowrap;
}

.sort-select {
  width: auto;
  min-width: 195px;
}

.state-box {
  text-align: center;
  padding: 40px 20px;
}

.products-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 18px;
}

.product-card {
  padding: 16px;
  display: flex;
  flex-direction: column;
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.product-card:hover {
  box-shadow: 0 6px 18px rgba(41, 35, 38, 0.08);
}

.card-image-link {
  display: block;
  margin-bottom: 14px;
}

.card-picture {
  height: 184px;
  padding: 12px;
  background: var(--surface-alt);
  border-radius: var(--radius-sm);
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
}

.card-picture img {
  max-width: 100%;
  max-height: 100%;
  width: auto;
  height: auto;
  object-fit: contain;
  object-position: center;
  display: block;
}

.card-category {
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.8px;
  color: var(--text-muted);
}

.card-title {
  font-size: 15px;
  line-height: 1.45;
  margin: 8px 0 10px;
  min-height: 44px;
}

.card-title a {
  color: var(--text-main);
}

.card-title a:hover {
  color: var(--burgundy);
}

.card-stock {
  font-size: 12px;
  font-weight: 600;
  margin-bottom: 16px;
}

.card-stock.in-stock {
  color: var(--success-text);
}

.card-stock.out-of-stock {
  color: var(--danger-text);
}

.card-bottom {
  margin-top: auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-top: 12px;
  border-top: 1px solid #F3ECE8;
}

.card-price {
  font-size: 19px;
  font-weight: 800;
  color: var(--text-main);
  white-space: nowrap;
}

.pagination {
  margin-top: 28px;
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.page-num {
  min-width: 40px;
  padding: 9px 12px;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  border: 0;
}

@media (max-width: 1020px) {
  .products-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 760px) {
  .catalog-layout {
    grid-template-columns: 1fr;
  }
  .intro-banner {
    padding: 20px;
  }
  .intro-text h1 {
    font-size: 25px;
  }
  .intro-mark {
    display: none;
  }
  .toolbar-right {
    width: 100%;
  }
  .sort-select {
    flex: 1;
    min-width: 0;
  }
}

@media (max-width: 480px) {
  .products-grid {
    grid-template-columns: 1fr;
  }
}
</style>
