<template>
  <main class="container orders-page">
    <div class="breadcrumb">
      <RouterLink to="/">Каталог товарів</RouterLink> / Особистий кабінет / Мої замовлення
    </div>

    <div class="heading-row">
      <div>
        <h1>Мої замовлення</h1>
        <p class="subtitle">Переглядайте склад власних замовлень, стан їхньої обробки та оплати.</p>
      </div>
      <RouterLink to="/" class="btn btn-outline">Перейти до каталогу →</RouterLink>
    </div>

    <div v-if="errorMessage" class="alert alert-error" role="alert">
      {{ errorMessage }}
    </div>

    <div v-if="loading" class="panel empty-panel" role="status">
      Завантаження історії ваших замовлень…
    </div>

    <div v-else-if="orders.length === 0" class="panel empty-panel">
      <p><strong>Ви ще не оформлювали замовлень.</strong></p>
      <p class="field-help">Оберіть канцелярські товари в каталозі, додайте їх до кошика та оформіть замовлення.</p>
      <RouterLink to="/" class="btn btn-primary" style="margin-top: 12px">
        Переглянути каталог
      </RouterLink>
    </div>

    <div v-else class="orders-list">
      <article v-for="(order, idx) in orders" :key="order.id" class="order-card panel">
        <div class="order-header">
          <div>
            <div class="order-number">Замовлення № {{ order.orderNumber }}</div>
            <div class="order-date">
              {{ formatDateTime(order.createdAt) }} · {{ order.totalQuantity }} од. товару
            </div>
          </div>

          <div class="order-badges">
            <span :class="order.isPaid ? 'badge badge-paid' : 'badge badge-unpaid'">
              {{ getPaymentBadgeText(order) }}
            </span>
            <span :class="statusBadgeClass(order.status)">
              {{ statusSymbol(order.status) }} {{ order.status }}
            </span>
            <span class="order-sum">{{ formatMoney(order.totalAmount) }}</span>
          </div>
        </div>

        <details :open="idx === 0">
          <summary>Склад замовлення та деталі отримання</summary>

          <div class="order-meta-grid">
            <div>
              <span class="meta-label">Отримувач:</span>
              <strong>{{ order.recipientFirstName }} {{ order.recipientLastName }}</strong>
              ({{ order.recipientPhone }}, {{ order.recipientEmail }})
            </div>
            <div>
              <span class="meta-label">Спосіб отримання:</span>
              <strong>{{ order.deliveryMethod }}</strong> — {{ order.pickupLocation }}
            </div>
            <div>
              <span class="meta-label">Оплата:</span>
              <strong>{{ order.paymentMethod }}</strong> ·
              <span>{{ getPaymentDetailText(order) }}</span>
            </div>
            <div v-if="order.comment">
              <span class="meta-label">Коментар:</span> {{ order.comment }}
            </div>
          </div>

          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">Товар (на момент замовлення)</th>
                  <th scope="col" class="num">Ціна за шт.</th>
                  <th scope="col" class="num">Кількість</th>
                  <th scope="col" class="num">Сума</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in order.items" :key="item.productId">
                  <td>{{ item.productName }}</td>
                  <td class="num">{{ formatMoney(item.priceAtPurchase) }}</td>
                  <td class="num">{{ item.quantity }} шт.</td>
                  <td class="num">{{ formatMoney(item.lineTotal) }}</td>
                </tr>
                <tr class="total-row">
                  <td colspan="3">Сума замовлення</td>
                  <td class="num">{{ formatMoney(order.totalAmount) }}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <p class="status-note">
            {{ getStatusExplanation(order) }}
          </p>
        </details>
      </article>
    </div>

    <p class="bottom-note">
      Назви та ціни товарів у замовленні зафіксовані на момент оформлення й не змінюються при подальшому редагуванні каталогу.
      Статус замовлення та відмітку про оплату оновлює адміністратор.
    </p>
  </main>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { apiRequest } from '../services/api'
import { formatMoney, formatDateTime, statusBadgeClass, statusSymbol } from '../utils/formatters'

const orders = ref([])
const loading = ref(true)
const errorMessage = ref('')

function getPaymentBadgeText(order) {
  if (order.isPaid) return '✓ Оплачено'
  if (order.status === 'Скасоване') return '✕ Оплату не здійснено'
  return '○ Оплата при отриманні'
}

function getPaymentDetailText(order) {
  if (order.isPaid) {
    return order.paidAt ? `Оплачено (${formatDateTime(order.paidAt)})` : 'Оплачено'
  }
  if (order.status === 'Скасоване') {
    return 'Оплату не здійснено, замовлення скасовано'
  }
  return 'Очікує оплати при видачі'
}

function getStatusExplanation(order) {
  switch (order.status) {
    case 'Нове':
      return 'Замовлення отримано системою та очікує на початок комплектації адміністратором.'
    case 'В обробці':
      return 'Адміністратор комплектує ваше замовлення для видачі у навчальному пункті самовивозу.'
    case 'Виконане':
      return 'Замовлення оплачено та успішно видано отримувачу. Обробку завершено.'
    case 'Скасоване':
      return 'Замовлення скасовано. Зарезервовані одиниці товарів повернуто до залишку складу.'
    default:
      return ''
  }
}

async function fetchOrders() {
  loading.value = true
  errorMessage.value = ''
  try {
    orders.value = await apiRequest('/api/orders')
  } catch (err) {
    errorMessage.value = err.message || 'Не вдалося завантажити ваші замовлення.'
  } finally {
    loading.value = false
  }
}

onMounted(fetchOrders)
</script>

<style scoped>
.orders-page {
  padding-bottom: 50px;
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
  font-size: 30px;
  margin: 0 0 8px;
}

.subtitle {
  color: var(--text-muted);
  margin: 0;
}

.empty-panel {
  text-align: center;
  padding: 42px 24px;
}

.order-card {
  margin-bottom: 18px;
}

.order-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 16px;
}

.order-number {
  font-weight: 800;
  font-size: 18px;
  margin-bottom: 6px;
}

.order-date {
  font-size: 13px;
  color: var(--text-muted);
}

.order-badges {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.order-sum {
  font-size: 20px;
  font-weight: 800;
  color: var(--burgundy-dark);
  white-space: nowrap;
}

details {
  margin-top: 18px;
  border-top: 1px solid var(--border-light);
}

summary {
  padding: 15px 0 4px;
  color: var(--burgundy);
  font-weight: 600;
  font-size: 14px;
  width: fit-content;
}

.order-meta-grid {
  margin-top: 12px;
  padding: 14px 16px;
  background: var(--surface-alt);
  border-radius: var(--radius-sm);
  font-size: 13px;
  line-height: 1.75;
}

.meta-label {
  color: var(--text-muted);
  margin-right: 6px;
}

.table-wrap {
  overflow-x: auto;
  margin-top: 14px;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}

th,
td {
  text-align: left;
  padding: 12px 10px;
  border-bottom: 1px solid var(--border-light);
}

th {
  color: var(--text-muted);
  font-weight: 600;
  background: var(--surface-alt);
}

.num {
  text-align: right;
  white-space: nowrap;
}

.total-row td {
  font-weight: 800;
  border-bottom: 0;
}

.status-note {
  margin: 14px 0 0;
  padding: 12px 15px;
  background: var(--burgundy-soft);
  border-radius: var(--radius-sm);
  color: var(--burgundy-dark);
  font-size: 13px;
  line-height: 1.6;
}

.bottom-note {
  color: var(--text-muted);
  font-size: 13px;
  line-height: 1.6;
  margin: 24px 0 0;
}
</style>
