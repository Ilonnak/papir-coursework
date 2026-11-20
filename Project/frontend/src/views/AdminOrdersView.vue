<template>
  <main class="container admin-orders-page">
    <div class="eyebrow">Адміністрування</div>
    <h1>Замовлення покупців</h1>
    <p class="subtitle">
      Переглядайте склад замовлень, контакти отримувачів, фіксуйте оплату при видачі та змінюйте дозволені статуси.
    </p>

    <div class="orders-layout">
      <section class="panel">
        <h2>Усі замовлення ({{ orders.length }})</h2>

        <div class="filters-row">
          <input
            v-model.trim="searchQuery"
            type="search"
            class="input"
            placeholder="Номер замовлення, ім’я, телефон або email покупця"
            aria-label="Пошук замовлення"
            @input="loadOrders"
          />
          <select
            v-model="statusFilter"
            class="select status-select"
            aria-label="Відбір за статусом"
            @change="loadOrders"
          >
            <option value="">Усі статуси</option>
            <option value="Нове">Нове</option>
            <option value="В обробці">В обробці</option>
            <option value="Виконане">Виконане</option>
            <option value="Скасоване">Скасоване</option>
          </select>
        </div>

        <div class="table-wrap">
          <table class="orders-table">
            <thead>
              <tr>
                <th>Замовлення</th>
                <th>Покупець / Отримувач</th>
                <th>Сума</th>
                <th>Оплата</th>
                <th>Статус</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="loading">
                <td colspan="6" class="empty-cell">Завантаження переліку замовлень…</td>
              </tr>
              <tr v-else-if="orders.length === 0">
                <td colspan="6" class="empty-cell">Замовлень за обраними умовами не знайдено.</td>
              </tr>
              <tr
                v-for="order in orders"
                v-else
                :key="order.id"
                :class="{ 'selected-row': selectedOrder?.id === order.id }"
              >
                <td>
                  <strong>{{ order.orderNumber }}</strong>
                  <small class="date-sub">{{ formatDateTime(order.createdAt) }}</small>
                </td>
                <td>
                  <div>{{ order.recipientFirstName }} {{ order.recipientLastName }}</div>
                  <small class="date-sub">{{ order.recipientEmail }}</small>
                </td>
                <td class="money">{{ formatMoney(order.totalAmount) }}</td>
                <td>
                  <span :class="order.isPaid ? 'badge badge-paid' : 'badge badge-unpaid'">
                    {{ getPaymentBadgeText(order) }}
                  </span>
                </td>
                <td>
                  <span :class="statusBadgeClass(order.status)">
                    {{ statusSymbol(order.status) }} {{ order.status }}
                  </span>
                </td>
                <td>
                  <button type="button" class="btn btn-outline btn-open" @click="selectOrder(order)">
                    Відкрити
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section v-if="selectedOrder" class="panel detail-panel">
        <div class="detail-head">
          <h2>Замовлення {{ selectedOrder.orderNumber }}</h2>
          <span :class="statusBadgeClass(selectedOrder.status)">
            {{ statusSymbol(selectedOrder.status) }} {{ selectedOrder.status }}
          </span>
        </div>

        <div class="info-box">
          <div>
            <span class="muted">Обліковий запис покупця:</span>
            <strong>{{ selectedOrder.buyerFullName }}</strong> ({{ selectedOrder.buyerEmail }})
          </div>
          <div>
            <span class="muted">Отримувач (знімок):</span>
            <strong>{{ selectedOrder.recipientFirstName }} {{ selectedOrder.recipientLastName }}</strong>
          </div>
          <div>
            <span class="muted">Телефон отримувача:</span>
            <strong>{{ selectedOrder.recipientPhone }}</strong>
          </div>
          <div>
            <span class="muted">Email отримувача:</span>
            <span>{{ selectedOrder.recipientEmail }}</span>
          </div>
          <div>
            <span class="muted">Дата оформлення:</span>
            <span>{{ formatDateTime(selectedOrder.createdAt) }}</span>
          </div>
          <div>
            <span class="muted">Отримання:</span>
            <span>{{ selectedOrder.deliveryMethod }} — {{ selectedOrder.pickupLocation }}</span>
          </div>
          <div>
            <span class="muted">Спосіб та стан оплати:</span>
            <strong>{{ selectedOrder.paymentMethod }}</strong> ·
            <span :class="selectedOrder.isPaid ? 'paid-text' : 'unpaid-text'">
              {{ getPaymentDetailText(selectedOrder) }}
            </span>
          </div>
          <div v-if="selectedOrder.comment">
            <span class="muted">Коментар покупця:</span>
            <span>{{ selectedOrder.comment }}</span>
          </div>
        </div>

        <h3>Склад замовлення</h3>
        <div class="table-wrap">
          <table class="items-table">
            <thead>
              <tr>
                <th>Товар (на момент оформлення)</th>
                <th class="money">Ціна</th>
                <th class="money">К-сть</th>
                <th class="money">Сума</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="item in selectedOrder.items" :key="item.productId">
                <td>{{ item.productName }}</td>
                <td class="money">{{ formatMoney(item.priceAtPurchase) }}</td>
                <td class="money">{{ item.quantity }}</td>
                <td class="money">{{ formatMoney(item.lineTotal) }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="total-bar">
          <span>Сума замовлення</span>
          <span>{{ formatMoney(selectedOrder.totalAmount) }}</span>
        </div>

        <!-- Окрема кнопка фіксації оплати під час видачі (доступна лише для замовлення «В обробці») -->
        <div
          v-if="!selectedOrder.isPaid && selectedOrder.status === 'В обробці'"
          class="payment-action-box"
        >
          <div>
            <strong>Оплата при отриманні</strong>
            <div class="field-help">Зафіксуйте отримання коштів у пункті самовивозу під час видачі замовлення.</div>
          </div>
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="submitting"
            @click="markAsPaid"
          >
            Зафіксувати оплату
          </button>
        </div>

        <div
          v-else-if="!selectedOrder.isPaid && selectedOrder.status === 'Нове'"
          class="alert alert-info"
          style="margin-top: 14px"
        >
          Оплату можна зафіксувати лише після прийняття замовлення у статус «В обробці».
        </div>

        <div class="status-form">
          <label for="next-status-select">Змінити статус замовлення</label>
          <div class="status-controls">
            <select
              id="next-status-select"
              v-model="nextStatus"
              class="select"
              :disabled="selectedOrder.allowedTransitions.length === 0 || submitting"
              @change="onNextStatusChange"
            >
              <option value="">
                {{ selectedOrder.allowedTransitions.length ? 'Оберіть новий статус' : 'Статус остаточний (переходів немає)' }}
              </option>
              <option v-for="st in selectedOrder.allowedTransitions" :key="st" :value="st">
                {{ st }}
              </option>
            </select>

            <button
              type="button"
              class="btn btn-primary"
              :disabled="!nextStatus || submitting || (nextStatus === 'Виконане' && !selectedOrder.isPaid && !confirmHandoverPayment)"
              @click="saveOrderStatus"
            >
              {{ submitting ? 'Збереження…' : 'Зберегти' }}
            </button>
          </div>

          <!-- Узгоджена операція завершення видачі з оплатою при отриманні -->
          <label
            v-if="nextStatus === 'Виконане' && !selectedOrder.isPaid"
            class="handover-check"
          >
            <input v-model="confirmHandoverPayment" type="checkbox" />
            <span>
              Підтверджую отримання оплати ({{ formatMoney(selectedOrder.totalAmount) }}) та фактичну видачу замовлення отримувачу.
            </span>
          </label>

          <p class="field-help" style="margin-top: 8px">
            <template v-if="selectedOrder.allowedTransitions.length > 0">
              Доступні переходи зі статусу «{{ selectedOrder.status }}»:
              <strong>{{ selectedOrder.allowedTransitions.join(' або ') }}</strong>.
              <template v-if="selectedOrder.isPaid && selectedOrder.status !== 'Виконане'">
                Оскільки замовлення вже оплачене, його скасування заборонено (повернення коштів у навчальному обсязі не передбачено).
              </template>
              <template v-else>
                У разі скасування неоплаченого замовлення залишки товарів автоматично повертаються на склад.
              </template>
            </template>
            <template v-else>
              Для виконаного або скасованого замовлення подальша зміна статусу не передбачена.
            </template>
          </p>

          <div v-if="feedbackMessage" class="alert alert-success" role="status">
            {{ feedbackMessage }}
          </div>
          <div v-if="errorMessage" class="alert alert-error" role="alert">
            {{ errorMessage }}
          </div>
        </div>
      </section>

      <section v-else class="panel empty-cell">
        Оберіть замовлення зі списку ліворуч для перегляду деталей та керування статусом.
      </section>
    </div>
  </main>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { apiRequest } from '../services/api'
import { formatMoney, formatDateTime, statusBadgeClass, statusSymbol } from '../utils/formatters'

const orders = ref([])
const selectedOrder = ref(null)
const loading = ref(false)
const searchQuery = ref('')
const statusFilter = ref('')

const nextStatus = ref('')
const confirmHandoverPayment = ref(false)
const submitting = ref(false)
const feedbackMessage = ref('')
const errorMessage = ref('')

function getPaymentBadgeText(order) {
  if (order.isPaid) return '✓ Оплачено'
  if (order.status === 'Скасоване') return '✕ Не оплачено'
  return '○ При отриманні'
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

async function loadOrders() {
  loading.value = true
  try {
    const params = new URLSearchParams()
    if (searchQuery.value) params.set('search', searchQuery.value)
    if (statusFilter.value) params.set('status', statusFilter.value)

    const list = await apiRequest(`/api/admin/orders?${params.toString()}`)
    orders.value = list

    if (selectedOrder.value) {
      const updatedSelected = list.find(o => o.id === selectedOrder.value.id)
      if (updatedSelected) {
        selectedOrder.value = updatedSelected
      } else if (list.length > 0) {
        selectOrder(list[0])
      } else {
        selectedOrder.value = null
      }
    } else if (list.length > 0) {
      selectOrder(list[0])
    }
  } catch (err) {
    errorMessage.value = err.message || 'Не вдалося завантажити список замовлень.'
  } finally {
    loading.value = false
  }
}

function selectOrder(order) {
  selectedOrder.value = order
  nextStatus.value = ''
  confirmHandoverPayment.value = false
  feedbackMessage.value = ''
  errorMessage.value = ''
}

function onNextStatusChange() {
  feedbackMessage.value = ''
  errorMessage.value = ''
  if (nextStatus.value !== 'Виконане') {
    confirmHandoverPayment.value = false
  }
}

async function markAsPaid() {
  if (!selectedOrder.value) return
  feedbackMessage.value = ''
  errorMessage.value = ''
  submitting.value = true
  try {
    const updated = await apiRequest(`/api/admin/orders/${selectedOrder.value.id}/payment`, {
      method: 'PATCH'
    })
    selectedOrder.value = updated
    feedbackMessage.value = `Оплату для замовлення ${updated.orderNumber} успішно зафіксовано.`
    await loadOrders()
  } catch (err) {
    errorMessage.value = err.message || 'Не вдалося зафіксувати оплату.'
  } finally {
    submitting.value = false
  }
}

async function saveOrderStatus() {
  if (!selectedOrder.value || !nextStatus.value) return
  feedbackMessage.value = ''
  errorMessage.value = ''
  submitting.value = true

  try {
    const updated = await apiRequest(`/api/admin/orders/${selectedOrder.value.id}/status`, {
      method: 'PATCH',
      body: JSON.stringify({
        newStatus: nextStatus.value,
        confirmPaymentOnHandover: Boolean(confirmHandoverPayment.value)
      })
    })
    selectedOrder.value = updated
    nextStatus.value = ''
    confirmHandoverPayment.value = false
    feedbackMessage.value = `Статус замовлення ${updated.orderNumber} змінено на «${updated.status}».`
    await loadOrders()
  } catch (err) {
    errorMessage.value = err.message || 'Не вдалося змінити статус замовлення.'
  } finally {
    submitting.value = false
  }
}

onMounted(loadOrders)
</script>

<style scoped>
.admin-orders-page {
  padding: 28px 0 50px;
}

.eyebrow {
  color: var(--burgundy);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 1.5px;
  text-transform: uppercase;
}

h1 {
  font-size: 29px;
  margin: 6px 0 8px;
}

.subtitle {
  color: var(--text-muted);
  margin: 0 0 24px;
  font-size: 14px;
}

.orders-layout {
  display: grid;
  grid-template-columns: minmax(0, 1.35fr) minmax(0, 1fr);
  gap: 22px;
  align-items: start;
}

.orders-layout > .panel {
  min-width: 0;
  max-width: 100%;
}

.panel h2 {
  font-size: 19px;
  margin: 0 0 16px;
}

.panel h3 {
  font-size: 16px;
  margin: 18px 0 12px;
}

.filters-row {
  display: flex;
  gap: 10px;
  margin-bottom: 18px;
  flex-wrap: wrap;
}

.filters-row .input {
  flex: 1 1 200px;
  min-width: 0;
}

.status-select {
  width: auto;
  min-width: 165px;
  max-width: 100%;
}

.table-wrap {
  width: 100%;
  max-width: 100%;
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.orders-table {
  min-width: 540px;
}

.items-table {
  min-width: 380px;
}

th,
td {
  padding: 12px 9px;
  border-bottom: 1px solid var(--border-light);
  vertical-align: middle;
  text-align: left;
}

th {
  color: var(--text-muted);
  font-weight: 600;
  background: var(--surface-alt);
}

.selected-row {
  background: #FAF0F3;
}

.date-sub {
  display: block;
  color: var(--text-muted);
  margin-top: 4px;
  font-size: 11px;
}

.money {
  white-space: nowrap;
  text-align: right;
}

.btn-open {
  padding: 6px 11px;
  font-size: 12px;
}

.detail-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.detail-head h2 {
  margin: 0;
}

.info-box {
  background: var(--surface-alt);
  border-radius: var(--radius-sm);
  padding: 14px 16px;
  margin-bottom: 16px;
  font-size: 13px;
  line-height: 1.8;
  overflow-wrap: anywhere;
  word-break: break-word;
}

.muted {
  color: var(--text-muted);
  margin-right: 6px;
}

.paid-text {
  color: var(--success-text);
  font-weight: 700;
}

.unpaid-text {
  color: var(--warning-text);
  font-weight: 600;
}

.total-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  font-weight: 800;
  font-size: 18px;
  padding: 16px 0;
  border-bottom: 1px solid var(--border-light);
  color: var(--burgundy-dark);
}

.payment-action-box {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 14px;
  margin-top: 16px;
  padding: 12px 14px;
  background: var(--burgundy-soft);
  border: 1px solid var(--burgundy-border);
  border-radius: var(--radius-sm);
  font-size: 13px;
  flex-wrap: wrap;
}

.status-form {
  margin-top: 18px;
}

.status-form label {
  display: block;
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 8px;
}

.status-controls {
  display: flex;
  gap: 10px;
}

.status-controls .select {
  flex: 1;
  min-width: 0;
}

.handover-check {
  display: flex !important;
  align-items: flex-start;
  gap: 9px;
  margin-top: 12px;
  padding: 10px 12px;
  background: var(--warning-bg);
  color: var(--warning-text);
  border-radius: var(--radius-sm);
  font-weight: 500 !important;
  cursor: pointer;
}

.handover-check input {
  margin-top: 3px;
}

.empty-cell {
  padding: 28px;
  text-align: center;
  color: var(--text-muted);
}

@media (max-width: 1020px) {
  .orders-layout {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 560px) {
  .admin-orders-page {
    padding: 18px 0 36px;
  }

  h1 {
    font-size: 24px;
  }

  .filters-row {
    flex-direction: column;
  }

  .filters-row .input,
  .status-select {
    width: 100%;
    min-width: 0;
    flex: none;
  }

  .payment-action-box {
    flex-direction: column;
    align-items: stretch;
  }

  .payment-action-box .btn {
    width: 100%;
  }

  .status-controls {
    flex-direction: column;
  }

  .status-controls .select,
  .status-controls .btn {
    width: 100%;
  }
}
</style>
