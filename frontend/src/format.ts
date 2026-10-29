import type { TicketPriority } from './api/types'

export function formatDate(iso: string) {
  return new Intl.DateTimeFormat('es-MX', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso))
}

/** "en 2 h 15 min" or "hace 30 min", for SLA due dates. */
export function formatRelative(iso: string, now = Date.now()) {
  const minutes = Math.round((new Date(iso).getTime() - now) / 60_000)
  const abs = Math.abs(minutes)
  const days = Math.floor(abs / 1440)
  const hours = Math.floor((abs % 1440) / 60)
  const mins = abs % 60
  const span = days > 0 ? `${days} d${hours ? ` ${hours} h` : ''}` : hours > 0 ? `${hours} h${mins ? ` ${mins} min` : ''}` : `${mins} min`
  return minutes >= 0 ? `en ${span}` : `hace ${span}`
}

/** Business minutes as the hours people think in: 30 min, 4 h, 1.5 h. */
export function formatMinutes(minutes: number) {
  return minutes < 60 ? `${minutes} min` : `${Number((minutes / 60).toFixed(2))} h`
}

export const priorities: TicketPriority[] = ['Urgent', 'High', 'Normal', 'Low']

export function priorityLabel(priority: TicketPriority) {
  return { Low: 'Baja', Normal: 'Normal', High: 'Alta', Urgent: 'Urgente' }[priority]
}

/** 512 B, 84 KB, 2.4 MB */
export function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
