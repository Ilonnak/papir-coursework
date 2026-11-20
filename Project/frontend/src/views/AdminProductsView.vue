<template>
  <main class="container admin-products-page">
    <div class="heading-row">
      <div>
        <div class="eyebrow">Адміністрування каталогу</div>
        <h1>Керування товарними пропозиціями</h1>
        <p class="subtitle">
          Налаштовуйте назви, категорії, описи, характеристики, ціни, зображення, залишки та видимість товарів.
        </p>
      </div>
      <button type="button" class="btn btn-primary" @click="startNewProduct">
        + Додати товар
      </button>
    </div>

    <div class="admin-layout">
      <div class="left-column">
        <section class="panel">
          <h2>Товари каталогу ({{ products.length }})</h2>

          <div class="toolbar">
            <input
              v-model.trim="searchQuery"
              type="search"
              class="input"
              placeholder="Пошук за назвою товару…"
              aria-label="Пошук товарів"
              @input="loadProducts"
            />
            <select
              v-model="visibilityFilter"
              class="select"
              aria-label="Фільтр за видимістю"
              @change="loadProducts"
            >
              <option value="all">Усі товари</option>
              <option value="active">Видимі в каталозі</option>
              <option value="hidden">Приховані</option>
            </select>
            <select
              v-model="categoryFilter"
              class="select"
              aria-label="Фільтр за категорією"
              @change="loadProducts"
            >
              <option :value="null">Усі категорії</option>
              <option v-for="c in categories" :key="c.id" :value="c.id">
                {{ c.name }}
              </option>
            </select>
          </div>

          <div v-if="listMessage" class="alert alert-info" role="status">
            {{ listMessage }}
          </div>

          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">Товар</th>
                  <th scope="col">Ціна</th>
                  <th scope="col">Залишок</th>
                  <th scope="col">Видимість</th>
                  <th scope="col">Дії</th>
                </tr>
              </thead>
              <tbody>
                <tr v-if="loading">
                  <td colspan="5" class="empty-cell">Завантаження списку товарів…</td>
                </tr>
                <tr v-else-if="products.length === 0">
                  <td colspan="5" class="empty-cell">Товарів за обраними умовами не знайдено.</td>
                </tr>
                <tr
                  v-for="item in products"
                  v-else
                  :key="item.id"
                  :class="{ 'selected-row': selectedId === item.id }"
                >
                  <td>
                    <div class="prod-cell">
                      <img
                        :src="resolveImageUrl(item.imagePath)"
                        :alt="item.name"
                        class="prod-thumb"
                        @error="onImgError"
                      />
                      <div>
                        <div class="product-name">{{ item.name }}</div>
                        <div class="category-sub">{{ item.categoryName }}</div>
                      </div>
                    </div>
                  </td>
                  <td class="nowrap">{{ formatMoney(item.price) }}</td>
                  <td class="nowrap">
                    <span :class="{ 'zero-stock': item.stockQuantity === 0 }">
                      {{ item.stockQuantity }} шт.
                    </span>
                  </td>
                  <td>
                    <span class="badge" :class="item.isActive ? 'badge-completed' : 'badge-unpaid'">
                      {{ item.isActive ? '● Видимий' : '○ Прихований' }}
                    </span>
                  </td>
                  <td class="actions-cell">
                    <button type="button" class="action-link" @click="selectProductForEdit(item)">
                      Редагувати
                    </button>
                    <button
                      type="button"
                      class="action-link secondary"
                      @click="toggleVisibility(item)"
                    >
                      {{ item.isActive ? 'Приховати' : 'Показати' }}
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </section>

        <!-- Секція керування категоріями -->
        <section class="panel categories-panel">
          <h2>Керування категоріями</h2>
          <p class="field-help" style="margin-bottom: 14px">
            Додавайте або перейменовуйте категорії. Непорожню категорію не можна видалити разом із товарами.
          </p>

          <form class="category-add-form" @submit.prevent="saveCategory">
            <input
              v-model.trim="categoryFormName"
              type="text"
              class="input"
              maxlength="100"
              placeholder="Назва нової або редагованої категорії (до 100 симв.)"
              aria-label="Назва категорії"
              required
            />
            <button type="submit" class="btn btn-primary">
              {{ editingCategoryId ? 'Зберегти назву' : '+ Додати категорію' }}
            </button>
            <button
              v-if="editingCategoryId"
              type="button"
              class="btn btn-outline"
              @click="cancelCategoryEdit"
            >
              Скасувати
            </button>
          </form>

          <div v-if="categoryFeedback" class="alert alert-success" role="status">
            {{ categoryFeedback }}
          </div>
          <div v-if="categoryError" class="alert alert-error" role="alert">
            {{ categoryError }}
          </div>

          <div class="categories-list">
            <div v-for="cat in categories" :key="cat.id" class="category-item">
              <div>
                <strong>{{ cat.name }}</strong>
                <span class="cat-badge">Усього товарів: {{ cat.productCount }} (активних: {{ cat.activeProductCount }})</span>
              </div>
              <div class="cat-actions">
                <button type="button" class="action-link" @click="startCategoryEdit(cat)">
                  Перейменувати
                </button>
                <button
                  type="button"
                  class="action-link danger"
                  :title="cat.productCount > 0 ? 'Неможливо видалити категорію, що містить товари' : 'Видалити порожню категорію'"
                  @click="removeCategory(cat)"
                >
                  Видалити
                </button>
              </div>
            </div>
          </div>
        </section>
      </div>

      <!-- Форма налаштування товарної пропозиції -->
      <section class="panel form-panel">
        <h2>
        {{
          selectedId === null
            ? 'Нова товарна пропозиція'
            : `Редагування пропозиції #${selectedId} (версія #${form.version})`
        }}
      </h2>

        <form @submit.prevent="saveProduct" novalidate>
          <div class="field">
            <label for="prod-name">Назва товару (до 255 символів) *</label>
            <input
              id="prod-name"
              v-model.trim="form.name"
              type="text"
              class="input"
              maxlength="255"
              required
            />
            <div class="field-help">{{ form.name.length }} / 255 символів</div>
          </div>

          <div class="field">
            <label for="prod-category">Категорія *</label>
            <select id="prod-category" v-model.number="form.categoryId" class="select" required>
              <option v-for="c in categories" :key="c.id" :value="c.id">
                {{ c.name }}
              </option>
            </select>
          </div>

          <div class="form-row">
            <div class="field">
              <label for="prod-price">Ціна, грн (від 0,01) *</label>
              <input
                id="prod-price"
                v-model.number="form.price"
                type="number"
                min="0.01"
                max="99999999.99"
                step="0.01"
                class="input"
                required
              />
            </div>

            <div class="field">
              <label for="prod-stock">Залишок, шт. (від 0) *</label>
              <input
                id="prod-stock"
                v-model.number="form.stockQuantity"
                type="number"
                min="0"
                max="1000000"
                step="1"
                class="input"
                required
              />
            </div>
          </div>

          <div class="field">
            <label for="prod-desc">Опис товару (до 4000 символів) *</label>
            <textarea
              id="prod-desc"
              v-model.trim="form.description"
              class="textarea"
              maxlength="4000"
              rows="4"
              required
            ></textarea>
          </div>

          <div class="field">
            <label for="prod-specs">Характеристики товару (окремо від опису) *</label>
            <textarea
              id="prod-specs"
              v-model.trim="form.specifications"
              class="textarea"
              maxlength="4000"
              rows="5"
              placeholder="Формат: А5&#10;Кількість аркушів: 80&#10;Лінування: Клітинка"
              required
            ></textarea>
            <div class="field-help">Формат: кожна характеристика з нового рядка («Назва: Значення»).</div>
          </div>

          <div class="field">
            <label for="prod-img-upload">Зображення товару (JPEG, PNG, WEBP, GIF до 5 МБ)</label>
            <div class="image-upload-row">
              <input
                id="prod-img-upload"
                type="file"
                accept="image/jpeg,image/png,image/webp,image/gif"
                class="input file-input"
                @change="handleFileUpload"
              />
              <span v-if="uploadingImage" class="field-help">Завантаження…</span>
            </div>

            <label for="prod-img-path" style="margin-top: 10px">Шлях до зображення</label>
            <input
              id="prod-img-path"
              v-model.trim="form.imagePath"
              type="text"
              class="input"
              maxlength="500"
              placeholder="/images/products/placeholder.svg"
            />
            <div class="image-preview">
              <img :src="resolveImageUrl(form.imagePath)" alt="Попередній перегляд зображення" @error="onImgError" />
            </div>
          </div>

          <label class="check-row">
            <input v-model="form.isActive" type="checkbox" />
            <span>Показувати товар у публічному каталозі</span>
          </label>

          <div v-if="formError" class="alert alert-error" role="alert">
            {{ formError }}
          </div>
          <div v-if="formSuccess" class="alert alert-success" role="status">
            {{ formSuccess }}
          </div>

          <button type="submit" class="btn btn-primary full-width" :disabled="savingProduct || uploadingImage">
            {{ savingProduct ? 'Збереження…' : 'Зберегти пропозицію' }}
          </button>
        </form>
      </section>
    </div>
  </main>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { apiRequest, resolveImageUrl } from '../services/api'
import { formatMoney } from '../utils/formatters'

const categories = ref([])
const products = ref([])
const loading = ref(false)
const searchQuery = ref('')
const visibilityFilter = ref('all')
const categoryFilter = ref(null)
const listMessage = ref('')

const selectedId = ref(null)
const savingProduct = ref(false)
const uploadingImage = ref(false)
const formError = ref('')
const formSuccess = ref('')

const form = ref({
  name: '',
  categoryId: 1,
  price: 100,
  stockQuantity: 10,
  version: 1,
  description: '',
  specifications: '',
  imagePath: '/images/products/placeholder.svg',
  isActive: true
})

const categoryFormName = ref('')
const editingCategoryId = ref(null)
const categoryFeedback = ref('')
const categoryError = ref('')

async function loadCategories() {
  categories.value = await apiRequest('/api/admin/categories')
  if (!form.value.categoryId && categories.value.length > 0) {
    form.value.categoryId = categories.value[0].id
  }
}

async function loadProducts() {
  loading.value = true
  try {
    const params = new URLSearchParams()
    if (searchQuery.value) params.set('search', searchQuery.value)
    if (visibilityFilter.value) params.set('visibility', visibilityFilter.value)
    if (categoryFilter.value) params.set('categoryId', String(categoryFilter.value))

    products.value = await apiRequest(`/api/admin/products?${params.toString()}`)
  } catch (err) {
    formError.value = err.message || 'Помилка завантаження товарів.'
  } finally {
    loading.value = false
  }
}

function selectProductForEdit(item) {
  selectedId.value = item.id
  formError.value = ''
  formSuccess.value = ''
  form.value = {
    name: item.name,
    categoryId: item.categoryId,
    price: item.price,
    stockQuantity: item.stockQuantity,
    version: item.version || 1,
    description: item.description,
    specifications: item.specifications,
    imagePath: item.imagePath,
    isActive: item.isActive
  }
}

function startNewProduct() {
  selectedId.value = null
  formError.value = ''
  formSuccess.value = ''
  form.value = {
    name: '',
    categoryId: categories.value[0]?.id || 1,
    price: 50,
    stockQuantity: 15,
    version: 1,
    description: '',
    specifications: 'Формат: А5\nМатеріал: Папір 80 г/м²',
    imagePath: '/images/products/placeholder.svg',
    isActive: true
  }
}

async function handleFileUpload(event) {
  const file = event.target.files?.[0]
  if (!file) return

  formError.value = ''
  uploadingImage.value = true
  try {
    const fd = new FormData()
    fd.append('file', file)
    const res = await apiRequest('/api/admin/products/upload-image', {
      method: 'POST',
      body: fd
    })
    form.value.imagePath = res.imagePath
    formSuccess.value = 'Зображення успішно завантажено на сервер.'
  } catch (err) {
    formError.value = err.message || 'Не вдалося завантажити зображення.'
  } finally {
    uploadingImage.value = false
    event.target.value = ''
  }
}

async function saveProduct() {
  formError.value = ''
  formSuccess.value = ''

  if (!form.value.name || !form.value.description || !form.value.specifications) {
    formError.value = 'Заповніть назву, опис та характеристики товару.'
    return
  }
  if (Number(form.value.price) <= 0) {
    formError.value = 'Ціна товару повинна бути додатною (від 0,01 грн).'
    return
  }
  if (!Number.isInteger(Number(form.value.stockQuantity)) || Number(form.value.stockQuantity) < 0) {
    formError.value = 'Залишок повинен бути цілим невід’ємним числом.'
    return
  }

  savingProduct.value = true
  try {
    const payload = {
      categoryId: Number(form.value.categoryId),
      name: form.value.name,
      description: form.value.description,
      specifications: form.value.specifications,
      price: Number(form.value.price),
      stockQuantity: Number(form.value.stockQuantity),
      version: Number(form.value.version || 1),
      isActive: Boolean(form.value.isActive),
      imagePath: form.value.imagePath || '/images/products/placeholder.svg'
    }

    if (selectedId.value === null) {
      const created = await apiRequest('/api/admin/products', {
        method: 'POST',
        body: JSON.stringify(payload)
      })
      selectedId.value = created.id
      form.value.version = created.version
      formSuccess.value = `Нову товарну пропозицію «${created.name}» успішно створено в PostgreSQL.`
    } else {
      const updated = await apiRequest(`/api/admin/products/${selectedId.value}`, {
        method: 'PUT',
        body: JSON.stringify(payload)
      })
      form.value.version = updated.version
      formSuccess.value = `Зміни для товару «${updated.name}» збережено (версія #${updated.version}).`
    }

    await Promise.all([loadProducts(), loadCategories()])
  } catch (err) {
    formError.value = err.message || 'Не вдалося зберегти товарну пропозицію.'
    if (err.code === 'PRODUCT_CONCURRENCY_CONFLICT' && err.data) {
      const fresh = err.data
      form.value = {
        name: fresh.name,
        categoryId: fresh.categoryId,
        price: fresh.price,
        stockQuantity: fresh.stockQuantity,
        version: fresh.version,
        description: fresh.description,
        specifications: fresh.specifications,
        imagePath: fresh.imagePath,
        isActive: fresh.isActive
      }
      await loadProducts()
    }
  } finally {
    savingProduct.value = false
  }
}

async function toggleVisibility(item) {
  listMessage.value = ''
  try {
    const updated = await apiRequest(`/api/admin/products/${item.id}/visibility`, {
      method: 'PATCH',
      body: JSON.stringify({ isActive: !item.isActive })
    })
    if (selectedId.value === item.id) {
      form.value.isActive = updated.isActive
      form.value.version = updated.version
    }
    listMessage.value = updated.isActive
      ? `Товар «${updated.name}» повернуто до публічного каталогу.`
      : `Товар «${updated.name}» приховано з публічного каталогу.`
    await Promise.all([loadProducts(), loadCategories()])
  } catch (err) {
    formError.value = err.message || 'Не вдалося змінити видимість товару.'
  }
}

function startCategoryEdit(cat) {
  editingCategoryId.value = cat.id
  categoryFormName.value = cat.name
  categoryError.value = ''
  categoryFeedback.value = ''
}

function cancelCategoryEdit() {
  editingCategoryId.value = null
  categoryFormName.value = ''
}

async function saveCategory() {
  categoryError.value = ''
  categoryFeedback.value = ''
  if (!categoryFormName.value) return

  try {
    if (editingCategoryId.value) {
      await apiRequest(`/api/admin/categories/${editingCategoryId.value}`, {
        method: 'PUT',
        body: JSON.stringify({ name: categoryFormName.value })
      })
      categoryFeedback.value = 'Назву категорії оновлено.'
    } else {
      await apiRequest('/api/admin/categories', {
        method: 'POST',
        body: JSON.stringify({ name: categoryFormName.value })
      })
      categoryFeedback.value = `Категорію «${categoryFormName.value}» створено.`
    }
    editingCategoryId.value = null
    categoryFormName.value = ''
    await Promise.all([loadCategories(), loadProducts()])
  } catch (err) {
    categoryError.value = err.message || 'Не вдалося зберегти категорію.'
  }
}

async function removeCategory(cat) {
  categoryError.value = ''
  categoryFeedback.value = ''
  try {
    await apiRequest(`/api/admin/categories/${cat.id}`, {
      method: 'DELETE'
    })
    categoryFeedback.value = `Категорію «${cat.name}» видалено.`
    await loadCategories()
  } catch (err) {
    categoryError.value = err.message || 'Неможливо видалити категорію.'
  }
}

function onImgError(e) {
  e.target.src = resolveImageUrl('/images/products/placeholder.svg')
}

onMounted(async () => {
  await Promise.all([loadCategories(), loadProducts()])
  if (products.value.length > 0) {
    selectProductForEdit(products.value[0])
  }
})
</script>

<style scoped>
.admin-products-page {
  padding: 28px 0 50px;
}

.eyebrow {
  color: var(--burgundy);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 1.5px;
  text-transform: uppercase;
}

.heading-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 20px;
  margin-bottom: 24px;
  flex-wrap: wrap;
}

h1 {
  margin: 6px 0 8px;
  font-size: 29px;
}

.subtitle {
  margin: 0;
  color: var(--text-muted);
  font-size: 14px;
}

.admin-layout {
  display: grid;
  grid-template-columns: 1.5fr 1fr;
  gap: 24px;
  align-items: start;
}

.left-column {
  display: flex;
  flex-direction: column;
  gap: 24px;
  min-width: 0;
}

.panel h2 {
  margin: 0 0 18px;
  font-size: 20px;
}

.toolbar {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.toolbar .input {
  flex: 1;
  min-width: 180px;
}

.toolbar .select {
  width: auto;
  min-width: 160px;
}

.table-wrap {
  overflow-x: auto;
}

table {
  width: 100%;
  min-width: 560px;
  border-collapse: collapse;
  font-size: 13px;
}

th,
td {
  padding: 13px 9px;
  text-align: left;
  border-bottom: 1px solid var(--border-light);
  vertical-align: middle;
}

th {
  background: var(--surface-alt);
  font-weight: 600;
  color: var(--text-muted);
}

.selected-row {
  background: #FAF0F3;
}

.prod-cell {
  display: flex;
  align-items: center;
  gap: 12px;
}

.prod-thumb {
  width: 48px;
  height: 48px;
  padding: 4px;
  border-radius: 6px;
  object-fit: contain;
  object-position: center;
  background: var(--surface-alt);
  border: 1px solid var(--border-light);
  flex-shrink: 0;
  display: block;
}

.product-name {
  font-weight: 700;
  line-height: 1.4;
  margin-bottom: 3px;
}

.category-sub {
  font-size: 12px;
  color: var(--text-muted);
}

.nowrap {
  white-space: nowrap;
}

.zero-stock {
  color: var(--danger-text);
  font-weight: 700;
}

.actions-cell {
  white-space: nowrap;
}

.action-link {
  display: block;
  border: 0;
  background: transparent;
  color: var(--burgundy);
  font-weight: 600;
  font-size: 12px;
  padding: 3px 0;
}

.action-link:hover {
  text-decoration: underline;
}

.action-link.secondary {
  color: var(--text-muted);
}

.action-link.danger {
  color: var(--danger-text);
}

.empty-cell {
  text-align: center;
  padding: 24px;
  color: var(--text-muted);
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.file-input {
  padding: 8px;
}

.image-preview {
  margin-top: 10px;
  height: 150px;
  padding: 10px;
  background: var(--surface-alt);
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  border: 1px solid var(--border-light);
}

.image-preview img {
  max-height: 100%;
  max-width: 100%;
  width: auto;
  height: auto;
  object-fit: contain;
  object-position: center;
  display: block;
}

.check-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 16px 0;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
}

.full-width {
  width: 100%;
}

/* Категорії */
.category-add-form {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.category-add-form .input {
  flex: 1;
  min-width: 200px;
}

.categories-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.category-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
  font-size: 13px;
  flex-wrap: wrap;
}

.cat-badge {
  margin-left: 10px;
  color: var(--text-muted);
  font-size: 12px;
}

.cat-actions {
  display: flex;
  gap: 14px;
}

@media (max-width: 1020px) {
  .admin-layout {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 540px) {
  .form-row {
    grid-template-columns: 1fr;
    gap: 0;
  }
  .toolbar .select {
    width: 100%;
  }
}
</style>
