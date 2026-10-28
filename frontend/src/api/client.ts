// Thin wrapper over fetch: same-origin cookies, JSON in/out, and errors carrying the API's message.

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  // FormData sets its own multipart Content-Type with the boundary.
  const form = body instanceof FormData
  const response = await fetch(url, {
    method,
    credentials: 'same-origin',
    headers: body === undefined || form ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : form ? body : JSON.stringify(body),
  })

  if (!response.ok) {
    throw new ApiError(response.status, await errorMessage(response))
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

async function errorMessage(response: Response): Promise<string> {
  try {
    const data = await response.json()
    if (typeof data?.message === 'string') return data.message
    if (data?.errors) return Object.values(data.errors).flat().join(' ')
    if (typeof data?.title === 'string') return data.title
  } catch {
    // Not JSON; fall through to a generic message.
  }
  if (response.status === 401) return 'Tu sesión terminó. Vuelve a iniciar sesión.'
  return 'Algo salió mal. Intenta de nuevo.'
}

export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: <T>(url: string, body: unknown) => request<T>('PUT', url, body),
  delete: <T>(url: string) => request<T>('DELETE', url),
  upload: <T>(url: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return request<T>('POST', url, form)
  },
}
