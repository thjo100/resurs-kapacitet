// Enkel fetch-wrapper mot .NET-API:t. Kakbaserad inloggning (same-origin).
export class ApiError extends Error {
  constructor(message, status, body) {
    super(message)
    this.status = status
    this.body = body
  }
}

async function request(method, url, body) {
  const opts = { method, headers: {}, credentials: 'same-origin' }
  if (body !== undefined) {
    opts.headers['Content-Type'] = 'application/json'
    opts.body = JSON.stringify(body)
  }
  let res
  try {
    res = await fetch(url, opts)
  } catch {
    throw new ApiError('Kunde inte nå servern. Kontrollera nätverket.', 0)
  }
  if (res.status === 204) return null
  const isJson = res.headers.get('content-type')?.includes('json')
  const data = isJson ? await res.json().catch(() => null) : await res.text()
  if (!res.ok) {
    const message =
      (data && typeof data === 'object' && (data.detail || data.title)) ||
      {
        401: 'Du behöver logga in.',
        403: 'Du saknar behörighet för detta.',
        404: 'Hittades inte.',
        409: 'Konflikt med befintlig data.',
        429: 'För många försök. Vänta en stund.',
      }[res.status] ||
      `Något gick fel (${res.status}).`
    throw new ApiError(message, res.status, data)
  }
  return data
}

export const api = {
  get: (url) => request('GET', url),
  post: (url, body) => request('POST', url, body ?? {}),
  put: (url, body) => request('PUT', url, body ?? {}),
  del: (url) => request('DELETE', url),
}
