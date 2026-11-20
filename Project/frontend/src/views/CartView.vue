<template>
  <main class="container cart-page">
    <div class="breadcrumb">
      <RouterLink to="/">Каталог товарів</RouterLink> / Кошик та оформлення замовлення
    </div>

    <h1>Ваш кошик</h1>
    <p class="subtitle">Перевірте обрані товари, їхню кількість та контакти отримувача перед оформленням.</p>

    <!-- Блок успішно оформленого замовлення -->
    <section v-if="createdOrder" class="panel order-success-panel" role="status">
      <div class="success-head">
        <div>
          <span class="badge badge-completed">✓ Замовлення успішно створено</span>
          <h2>Замовлення № {{ createdOrder.orderNumber }}</h2>
        </div>
        <span :class="statusBadgeClass(createdOrder.status)">
          {{ statusSymbol(createdOrder.status) }} {{ createdOrder.status }}
        </span>
      </div>

      <div class="success-meta">
        <div>
          <span class="meta-label">Отримувач:</span>
          <strong>{{ createdOrder.recipientFirstName }} {{ createdOrder.recipientLastName }}</strong>
          ({{ createdOrder.recipientPhone }}, {{ createdOrder.recipientEmail }})
        </div>
        <div>
          <span class="meta-label">Спосіб отримання:</span>
          <strong>{{ createdOrder.deliveryMethod }}</strong> — {{ createdOrder.pickupLocation }}
        </div>
        <div>
          <span class="meta-label">Спосіб оплати:</span>
          <strong>{{ createdOrder.paymentMethod }}</strong>
          ({{ createdOrder.isPaid ? 'Оплачено' : 'Очікує оплати при отриманні' }})
        </div>
        <div v-if="createdOrder.comment">
          <span class="meta-label">Коментар:</span> {{ createdOrder.comment }}
        </div>
      </div>

      <div class="table-wrap">
        <table class="order-summary-table">
          <thead>
            <tr>
              <th>Товар</th>
              <th class="num">Ціна за шт.</th>
              <th class="num">Кількість</th>
              <th class="num">Сума</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in createdOrder.items" :key="item.productId">
              <td>{{ item.productName }}</td>
              <td class="num">{{ formatMoney(item.priceAtPurchase) }}</td>
              <td class="num">{{ item.quantity }} шт.</td>
              <td class="num">{{ formatMoney(item.lineTotal) }}</td>
            </tr>
            <tr class="total-row">
              <td colspan="3">Сума замовлення</td>
              <td class="num">{{ formatMoney(createdOrder.totalAmount) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="success-actions">
        <RouterLink to="/orders" class="btn btn-primary">
          Перейти до «Мої замовлення»
        </RouterLink>
        <RouterLink to="/" class="btn btn-outline">
          Продовжити покупки в каталозі
        </RouterLink>
      </div>
    </section>

    <div v-else class="cart-layout">
      <section class="panel">
        <h2>Обрані товари</h2>

        <div v-if="cartStore.warnings.length > 0" class="alert alert-warning" role="alert">
          <strong>Увага! Перевірте позиції кошика перед оформленням:</strong>
          <ul style="margin: 6px 0 0; padding-left: 20px">
            <li v-for="(w, i) in cartStore.warnings" :key="i">{{ w }}</li>
          </ul>
        </div>

        <div v-if="itemError" class="alert alert-error" role="alert">
          {{ itemError }}
        </div>

        <div v-if="cartStore.loading" class="empty-cart" role="status">
          Завантаження вашого кошика…
        </div>

        <div v-else-if="cartStore.items.length === 0" class="empty-cart">
          <p><strong>У кошику поки немає товарів.</strong></p>
          <p class="field-help">Перейдіть до каталогу, щоб обрати канцелярське приладдя.</p>
          <RouterLink to="/" class="btn btn-primary" style="margin-top: 12px">
            Перейти до каталогу →
          </RouterLink>
        </div>

        <div v-else class="cart-items-list">
          <article
            v-for="item in cartStore.items"
            :key="item.productId"
            class="cart-item"
            :class="{ 'cart-item-issue': item.hasIssue }"
          >
            <RouterLink :to="`/products/${item.productId}`" class="item-picture">
              <img :src="resolveImageUrl(item.imagePath)" :alt="item.productName" @error="onImgError" />
            </RouterLink>

            <div class="item-body">
              <div class="item-head">
                <div>
                  <span class="item-cat">{{ item.categoryName }}</span>
                  <h3>
                    <RouterLink :to="`/products/${item.productId}`">{{ item.productName }}</RouterLink>
                  </h3>
                </div>
                <button type="button" class="btn-remove" @click="removeCartItem(item.productId)">
                  Видалити
                </button>
              </div>

              <div class="unit-price">
                {{ formatMoney(item.unitPrice) }} / шт.
                <span v-if="item.isProductActive" class="stock-hint">
                  · Доступно: {{ item.availableStock }} шт.
                </span>
              </div>

              <div v-if="item.issueDescription" class="field-error" style="margin-top: 6px">
                ⚠ {{ item.issueDescription }}
              </div>

              <div class="item-controls">
                <div class="qty-control">
                  <button
                    type="button"
                    aria-label="Зменшити кількість"
                    :disabled="!item.isProductActive || item.quantity <= 1 || busyProductId === item.productId"
                    @click="stepQuantity(item, -1)"
                  >
                    −
                  </button>
                  <input
                    type="number"
                    min="1"
                    :max="Math.max(1, item.availableStock)"
                    step="1"
                    :value="item.quantity"
                    :disabled="!item.isProductActive || busyProductId === item.productId"
                    aria-label="Кількість товару"
                    @change="onManualQuantityChange(item, $event)"
                  />
                  <button
                    type="button"
                    aria-label="Збільшити кількість"
                    :disabled="!item.isProductActive || item.quantity >= item.availableStock || busyProductId === item.productId"
                    @click="stepQuantity(item, 1)"
                  >
                    +
                  </button>
                </div>

                <span class="line-total">{{ formatMoney(item.lineTotal) }}</span>
              </div>
            </div>
          </article>
        </div>

        <RouterLink to="/" class="back-link">← Продовжити покупки</RouterLink>
      </section>

      <section class="panel checkout-panel">
        <h2>Підсумок та оформлення</h2>

        <div class="summary-line">
          <span>Кількість одиниць</span>
          <strong>{{ cartStore.totalQuantity }} шт.</strong>
        </div>
        <div class="summary-line">
          <span>Вартість товарів</span>
          <strong>{{ formatMoney(cartStore.totalAmount) }}</strong>
        </div>
        <div class="grand-total">
          <span>Разом</span>
          <span>{{ formatMoney(cartStore.totalAmount) }}</span>
        </div>

        <div class="pickup-box">
          <div class="pickup-badge">Навчальний пункт видачі</div>
          <strong>{{ pickupInfo.title }}</strong>
          <p>{{ pickupInfo.address }}</p>
          <p v-if="pickupInfo.schedule" class="pickup-meta">Графік: {{ pickupInfo.schedule }}</p>
          <div class="delivery-payment-tags">
            <span>Отримання: <strong>Самовивіз</strong></span>
            <span>Оплата: <strong>При отриманні</strong></span>
          </div>
        </div>

        <form @submit.prevent="submitOrder" novalidate>
          <h3 class="form-section-title">Контакти отримувача</h3>

          <div class="form-row">
            <div class="field">
              <label for="rec-firstName">Ім’я отримувача *</label>
              <input
                id="rec-firstName"
                v-model.trim="checkoutForm.recipientFirstName"
                type="text"
                class="input"
                maxlength="100"
                required
                @input="onFormChange"
              />
            </div>
            <div class="field">
              <label for="rec-lastName">Прізвище отримувача *</label>
              <input
                id="rec-lastName"
                v-model.trim="checkoutForm.recipientLastName"
                type="text"
                class="input"
                maxlength="100"
                required
                @input="onFormChange"
              />
            </div>
          </div>

          <div class="field">
            <label for="rec-phone">Контактний телефон *</label>
            <input
              id="rec-phone"
              v-model.trim="checkoutForm.recipientPhone"
              type="tel"
              class="input"
              maxlength="30"
              placeholder="+380501234567"
              required
              @input="onFormChange"
            />
          </div>

          <div class="field">
            <label for="rec-email">Електронна пошта *</label>
            <input
              id="rec-email"
              v-model.trim="checkoutForm.recipientEmail"
              type="email"
              class="input"
              maxlength="255"
              required
              @input="onFormChange"
            />
          </div>

          <div class="field">
            <label for="rec-comment">Коментар до замовлення (необов’язково)</label>
            <textarea
              id="rec-comment"
              v-model.trim="checkoutForm.comment"
              class="textarea"
              maxlength="500"
              rows="2"
              placeholder="Побажання щодо комплектації або часу візиту до пункту самовивозу"
            ></textarea>
          </div>

          <label class="confirm-check">
            <input v-model="confirmed" type="checkbox" :disabled="!cartStore.canCheckout" />
            <span>
              Я перевірив(-ла) склад замовлення, кількість товарів і підтверджую оформлення на суму
              <strong>{{ formatMoney(cartStore.totalAmount) }}</strong> з оплатою при отриманні.
            </span>
          </label>

          <div v-if="checkoutError" class="alert alert-error" role="alert">
            {{ checkoutError }}
          </div>

          <button
            type="submit"
            class="btn btn-primary checkout-btn"
            :disabled="!cartStore.canCheckout || !confirmed || submittingOrder"
          >
            {{ submittingOrder ? 'Оформлення замовлення…' : 'Оформити замовлення' }}
          </button>

          <p class="checkout-note">
            Онлайн-оплата на сайті не проводиться. Після оформлення замовлення отримає статус «Нове» і буде передано адміністратору для підготовки до видачі.
          </p>
        </form>
      </section>
    </div>
  </main>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { apiRequest, resolveImageUrl } from '../services/api'
import { formatMoney, statusBadgeClass, statusSymbol } from '../utils/formatters'
import { useAuthStore } from '../stores/auth'
import { useCartStore } from '../stores/cart'

const authStore = useAuthStore()
const cartStore = useCartStore()

const pickupInfo = ref({
  title: 'Навчальний демонстраційний пункт самовивозу «папір.»',
  address: 'Демонстраційна локація для курсової роботи',
  schedule: 'Пн–Пт: 10:00–18:00',
  paymentNote: 'Оплата при отриманні.'
})

const checkoutForm = ref({
  recipientFirstName: authStore.user?.firstName || '',
  recipientLastName: authStore.user?.lastName || '',
  recipientPhone: '+380501234567',
  recipientEmail: authStore.user?.email || '',
  comment: ''
})

const confirmed = ref(false)
const busyProductId = ref(null)
const itemError = ref('')
const checkoutError = ref('')
const submittingOrder = ref(false)
const createdOrder = ref(null)
const currentIdempotencyKey = ref(generateIdempotencyKey())

function generateIdempotencyKey() {
  return `ord-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}

function resetConfirmation() {
  confirmed.value = false
  checkoutError.value = ''
  currentIdempotencyKey.value = generateIdempotencyKey()
}

function onFormChange() {
  checkoutError.value = ''
  currentIdempotencyKey.value = generateIdempotencyKey()
}

async function loadPickupInfo() {
  try {
    pickupInfo.value = await apiRequest('/api/orders/pickup-info')
  } catch {
    // Використовуємо базові значення у разі помилки
  }
}

async function stepQuantity(item, delta) {
  itemError.value = ''
  resetConfirmation()
  const next = item.quantity + delta
  if (next < 1 || next > item.availableStock) return

  busyProductId.value = item.productId
  try {
    await cartStore.updateQuantity(item.productId, next)
  } catch (err) {
    itemError.value = err.message || 'Не вдалося оновити кількість товару.'
    await cartStore.fetchCart()
  } finally {
    busyProductId.value = null
  }
}

async function onManualQuantityChange(item, event) {
  itemError.value = ''
  resetConfirmation()
  const val = Number(event.target.value)
  if (!Number.isInteger(val) || val < 1 || val > item.availableStock) {
    event.target.value = item.quantity
    itemError.value = `Вкажіть цілу кількість від 1 до ${item.availableStock} шт. для «${item.productName}».`
    return
  }

  busyProductId.value = item.productId
  try {
    await cartStore.updateQuantity(item.productId, val)
  } catch (err) {
    itemError.value = err.message || 'Не вдалося оновити кількість.'
    await cartStore.fetchCart()
  } finally {
    busyProductId.value = null
  }
}

async function removeCartItem(productId) {
  itemError.value = ''
  resetConfirmation()
  busyProductId.value = productId
  try {
    await cartStore.removeItem(productId)
  } catch (err) {
    itemError.value = err.message || 'Не вдалося видалити товар із кошика.'
  } finally {
    busyProductId.value = null
  }
}

async function submitOrder() {
  checkoutError.value = ''
  if (!cartStore.canCheckout || !confirmed.value || submittingOrder.value) return

  if (
    !checkoutForm.value.recipientFirstName ||
    !checkoutForm.value.recipientLastName ||
    !checkoutForm.value.recipientPhone ||
    !checkoutForm.value.recipientEmail
  ) {
    checkoutError.value = 'Будь ласка, заповніть ім’я, прізвище, телефон та email отримувача.'
    return
  }

  submittingOrder.value = true
  try {
    const order = await apiRequest('/api/orders', {
      method: 'POST',
      body: JSON.stringify({
        recipientFirstName: checkoutForm.value.recipientFirstName,
        recipientLastName: checkoutForm.value.recipientLastName,
        recipientPhone: checkoutForm.value.recipientPhone,
        recipientEmail: checkoutForm.value.recipientEmail,
        comment: checkoutForm.value.comment || null,
        cartSnapshotToken: cartStore.cartSnapshotToken,
        confirmedItems: cartStore.items.map(i => ({
          productId: i.productId,
          quantity: i.quantity,
          unitPrice: i.unitPrice
        })),
        expectedTotalAmount: cartStore.totalAmount,
        idempotencyKey: currentIdempotencyKey.value
      })
    })

    createdOrder.value = order
    await cartStore.fetchCart()
    window.scrollTo({ top: 0, behavior: 'smooth' })
  } catch (err) {
    checkoutError.value = err.message || 'Не вдалося оформити замовлення.'
    // Якщо змінився склад, ціна або залишок на складі — оновлюємо кошик і скидаємо прапорець підтвердження, щоб покупець підтвердив новий розрахунок
    if (
      err.code === 'CART_OR_PRICE_CHANGED' ||
      err.code === 'PRICE_CHANGED' ||
      err.code === 'INSUFFICIENT_STOCK' ||
      err.code === 'PRODUCT_INACTIVE' ||
      err.code === 'IDEMPOTENCY_KEY_PAYLOAD_MISMATCH'
    ) {
      confirmed.value = false
      currentIdempotencyKey.value = generateIdempotencyKey()
      await cartStore.fetchCart()
    }
  } finally {
    submittingOrder.value = false
  }
}

function onImgError(e) {
  e.target.src = resolveImageUrl('/images/products/placeholder.svg')
}

onMounted(async () => {
  await Promise.all([cartStore.fetchCart(), loadPickupInfo()])
})
</script>

<style scoped>
.cart-page {
  padding-bottom: 50px;
}

h1 {
  font-size: 30px;
  margin: 0 0 8px;
}

.subtitle {
  color: var(--text-muted);
  margin: 0 0 26px;
}

.cart-layout {
  display: grid;
  grid-template-columns: 1.45fr 1fr;
  gap: 26px;
  align-items: start;
}

.panel h2 {
  font-size: 20px;
  margin: 0 0 18px;
}

.empty-cart {
  padding: 28px 0;
  color: var(--text-muted);
}

.cart-item {
  display: grid;
  grid-template-columns: 88px 1fr;
  gap: 18px;
  padding: 20px 0;
  border-bottom: 1px solid var(--border-light);
}

.cart-item:first-child {
  padding-top: 0;
}

.cart-item:last-child {
  border-bottom: 0;
}

.cart-item-issue {
  background: #FDF7F8;
  padding: 14px;
  border-radius: var(--radius-sm);
  border: 1px solid #EBC3CA;
  margin-bottom: 10px;
}

.item-picture {
  width: 88px;
  height: 92px;
  padding: 6px;
  border-radius: var(--radius-sm);
  background: var(--surface-alt);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.item-picture img {
  max-width: 100%;
  max-height: 100%;
  width: auto;
  height: auto;
  object-fit: contain;
  object-position: center;
  display: block;
}

.item-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 10px;
}

.item-cat {
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.7px;
  color: var(--text-muted);
}

.item-head h3 {
  margin: 4px 0 6px;
  font-size: 16px;
  line-height: 1.4;
}

.item-head h3 a {
  color: var(--text-main);
}

.item-head h3 a:hover {
  color: var(--burgundy);
}

.btn-remove {
  border: 0;
  background: transparent;
  color: var(--danger-text);
  font-size: 13px;
  padding: 2px 4px;
}

.btn-remove:hover {
  text-decoration: underline;
}

.unit-price {
  font-size: 13px;
  color: var(--text-muted);
}

.item-controls {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  margin-top: 14px;
  flex-wrap: wrap;
}

.qty-control {
  display: flex;
  border: 1px solid #D8CBC5;
  border-radius: var(--radius-sm);
  overflow: hidden;
  background: var(--surface);
}

.qty-control button {
  background: var(--burgundy-soft);
  color: var(--burgundy-dark);
  border: 0;
  width: 34px;
  padding: 7px 0;
  font-weight: 700;
}

.qty-control input {
  width: 52px;
  min-width: 0;
  text-align: center;
  border: 0;
  font-weight: 600;
}

.line-total {
  font-weight: 800;
  font-size: 18px;
}

.back-link {
  display: inline-block;
  margin-top: 18px;
  font-size: 14px;
  font-weight: 600;
}

.summary-line {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  margin: 12px 0;
  font-size: 14px;
}

.grand-total {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  border-top: 1px solid var(--border-light);
  padding-top: 16px;
  margin: 16px 0;
  font-size: 22px;
  font-weight: 800;
  color: var(--burgundy-dark);
}

.pickup-box {
  background: var(--burgundy-soft);
  border: 1px solid var(--burgundy-border);
  padding: 14px 16px;
  border-radius: var(--radius-sm);
  margin: 18px 0;
  font-size: 13px;
  line-height: 1.55;
}

.pickup-badge {
  display: inline-block;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.8px;
  font-weight: 700;
  color: var(--burgundy);
  margin-bottom: 4px;
}

.pickup-box strong {
  display: block;
  color: var(--text-main);
}

.pickup-box p {
  margin: 4px 0;
  color: var(--text-muted);
}

.delivery-payment-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px solid var(--burgundy-border);
  color: var(--burgundy-dark);
}

.form-section-title {
  font-size: 16px;
  margin: 18px 0 12px;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.confirm-check {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--text-main);
  margin: 16px 0;
  cursor: pointer;
}

.confirm-check input {
  margin-top: 3px;
  flex-shrink: 0;
}

.checkout-btn {
  width: 100%;
  padding: 14px;
  font-size: 15px;
}

.checkout-note {
  font-size: 12px;
  line-height: 1.6;
  color: var(--text-muted);
  margin: 12px 0 0;
}

/* Успішне замовлення */
.order-success-panel {
  max-width: 820px;
  margin: 0 auto;
}

.success-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  flex-wrap: wrap;
  margin-bottom: 18px;
}

.success-head h2 {
  margin: 10px 0 0;
  font-size: 24px;
}

.success-meta {
  background: var(--surface-alt);
  padding: 16px;
  border-radius: var(--radius-sm);
  font-size: 14px;
  line-height: 1.8;
  margin-bottom: 20px;
}

.meta-label {
  color: var(--text-muted);
  margin-right: 6px;
}

.table-wrap {
  overflow-x: auto;
}

.order-summary-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}

.order-summary-table th,
.order-summary-table td {
  padding: 12px 10px;
  border-bottom: 1px solid var(--border-light);
  text-align: left;
}

.order-summary-table th {
  background: var(--surface-alt);
  color: var(--text-muted);
  font-weight: 600;
}

.order-summary-table .num {
  text-align: right;
  white-space: nowrap;
}

.total-row td {
  font-weight: 800;
  font-size: 16px;
  border-bottom: 0;
}

.success-actions {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 22px;
}

@media (max-width: 860px) {
  .cart-layout {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 500px) {
  .cart-item {
    grid-template-columns: 64px 1fr;
    gap: 12px;
  }
  .form-row {
    grid-template-columns: 1fr;
    gap: 0;
  }
}
</style>
