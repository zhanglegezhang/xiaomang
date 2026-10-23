export type PlainPreferences = { fontSize: number; wrapping: boolean }
export function normalizePlainPreferences(value: unknown): PlainPreferences {
  const v = value && typeof value === 'object' ? value as Partial<PlainPreferences> : {}
  return {
    fontSize: typeof v.fontSize === 'number' && Number.isInteger(v.fontSize) && v.fontSize >= 12 && v.fontSize <= 28 ? v.fontSize : 14,
    wrapping: typeof v.wrapping === 'boolean' ? v.wrapping : true,
  }
}
export function readPlainPreferences(storage: Pick<Storage, 'getItem'>, root: string): PlainPreferences {
  try { return normalizePlainPreferences(JSON.parse(storage.getItem('diary-plain-preferences:' + root) || '{}')) }
  catch { return normalizePlainPreferences(null) }
}
export function writePlainPreferences(storage: Pick<Storage, 'setItem'>, root: string, value: PlainPreferences): boolean {
  if (!root) return false
  try { storage.setItem('diary-plain-preferences:' + root, JSON.stringify(normalizePlainPreferences(value))); return true }
  catch { return false }
}
