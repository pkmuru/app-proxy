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

/** Bodies are captured as raw strings; JSON is far easier to read indented. */
export function prettyBody(body: string | null): string {
  if (!body) return ''

  try {
    return JSON.stringify(JSON.parse(body), null, 2)
  } catch {
    return body
  }
}
