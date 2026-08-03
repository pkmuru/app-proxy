const METHOD_COLORS: Record<string, string> = {
  GET: 'teal',
  POST: 'blue',
  PUT: 'yellow',
  PATCH: 'grape',
  DELETE: 'red',
}

export function methodColor(method: string): string {
  return METHOD_COLORS[method.toUpperCase()] ?? 'gray'
}

export function statusColor(status: number): string {
  if (status >= 500) return 'red'
  if (status >= 400) return 'orange'
  if (status >= 300) return 'yellow'
  return 'teal'
}

export function authColor(auth: string): string {
  if (auth === 'obo') return 'violet'
  if (auth === 'passthrough') return 'blue'
  return 'gray'
}

export function formatTime(iso: string): string {
  const at = new Date(iso)
  const time = at.toLocaleTimeString(undefined, { hour12: false })
  return `${time}.${String(at.getMilliseconds()).padStart(3, '0')}`
}

/**
 * A body indented for reading, or null when it is not JSON — which is also how we know a body sent
 * as `application/json` is not one. Both questions come from the single parse.
 */
export function formatJson(body: string | null): string | null {
  if (!body) return null

  try {
    return JSON.stringify(JSON.parse(body), null, 2)
  } catch {
    return null
  }
}

/** Header lookup that ignores case, as HTTP does and a plain object does not. */
export function header(headers: Record<string, string>, name: string): string | undefined {
  const wanted = name.toLowerCase()
  return Object.entries(headers).find(([key]) => key.toLowerCase() === wanted)?.[1]
}
