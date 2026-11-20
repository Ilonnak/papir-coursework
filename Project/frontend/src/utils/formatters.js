export function formatMoney(value) {
  const num = Number(value) || 0
  return (
    num.toLocaleString('uk-UA', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2
    }) + ' грн'
  )
}

export function formatDateTime(isoString) {
  if (!isoString) return '—'
  const date = new Date(isoString)
  if (Number.isNaN(date.getTime())) return isoString
  return date.toLocaleString('uk-UA', {
    day: '2-digit',
    month: 'long',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  })
}

export function statusBadgeClass(status) {
  switch (status) {
    case 'Нове':
      return 'badge badge-new'
    case 'В обробці':
      return 'badge badge-processing'
    case 'Виконане':
      return 'badge badge-completed'
    case 'Скасоване':
      return 'badge badge-cancelled'
    default:
      return 'badge badge-new'
  }
}

export function statusSymbol(status) {
  switch (status) {
    case 'Нове':
      return '●'
    case 'В обробці':
      return '◐'
    case 'Виконане':
      return '✓'
    case 'Скасоване':
      return '✕'
    default:
      return '●'
  }
}
