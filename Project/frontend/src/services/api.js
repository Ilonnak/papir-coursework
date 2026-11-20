const API_BASE = import.meta.env.VITE_API_BASE_URL || ''

export class ApiError extends Error {
  constructor(message, status, code = null, data = null, errors = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.data = data
    this.errors = errors
  }
}

export async function apiRequest(path, options = {}) {
  const token = localStorage.getItem('papir_jwt')
  const headers = { ...(options.headers || {}) }

  if (!(options.body instanceof FormData) && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json'
  }

  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  let response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      ...options,
      headers
    })
  } catch {
    throw new ApiError(
      'Сервер тимчасово недоступний. Перевірте, чи запущено бекенд застосунку «папір.» (http://localhost:5150).',
      0,
      'NETWORK_ERROR'
    )
  }

  let payload = null
  const contentType = response.headers.get('content-type') || ''
  if (contentType.includes('application/json')) {
    try {
      payload = await response.json()
    } catch {
      payload = null
    }
  }

  if (!response.ok) {
    if (response.status === 401) {
      window.dispatchEvent(new CustomEvent('papir:unauthorized'))
    }
    const msg =
      payload?.message ||
      (response.status === 401
        ? 'Необхідна авторизація або сесія завершилася.'
        : response.status === 403
          ? 'Недостатньо прав доступу для виконання цієї дії.'
          : response.status === 404
            ? 'Запитуваний ресурс не знайдено.'
            : 'Помилка виконання запиту.')
    throw new ApiError(msg, response.status, payload?.code || null, payload?.data || null, payload?.errors || null)
  }

  return payload
}

export function resolveImageUrl(imagePath) {
  if (!imagePath) return `${API_BASE}/images/products/placeholder.svg`
  if (imagePath.startsWith('http://') || imagePath.startsWith('https://')) {
    return imagePath
  }
  const normalized = imagePath.startsWith('/') ? imagePath : `/${imagePath}`
  return `${API_BASE}${normalized}`
}
