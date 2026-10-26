import type { ButtonHTMLAttributes, ReactNode } from 'react'
import type { TicketStatus } from '../api/types'

// Words a requester understands; never "issue", "workflow" or "segment".
const statusLabels: Record<TicketStatus, string> = {
  New: 'Nueva',
  InProgress: 'En atención',
  WaitingOnRequester: 'Esperando tu respuesta',
  Resolved: 'Resuelta',
  Closed: 'Cerrada',
}

const statusStyles: Record<TicketStatus, string> = {
  New: 'bg-sky-100 text-sky-800',
  InProgress: 'bg-amber-100 text-amber-800',
  WaitingOnRequester: 'bg-violet-100 text-violet-800',
  Resolved: 'bg-emerald-100 text-emerald-800',
  Closed: 'bg-slate-200 text-slate-700',
}

export function StatusBadge({ status }: { status: TicketStatus }) {
  return (
    <span className={`inline-flex rounded-full px-3 py-1 text-sm font-medium ${statusStyles[status]}`}>
      {statusLabels[status]}
    </span>
  )
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'secondary' }

export function Button({ variant = 'primary', className = '', ...props }: ButtonProps) {
  const look =
    variant === 'primary'
      ? 'bg-brand-600 text-white hover:bg-brand-700 disabled:bg-slate-300'
      : 'border border-slate-300 bg-white text-slate-800 hover:bg-slate-100 disabled:text-slate-400'
  return (
    <button
      className={`rounded-lg px-4 py-2.5 text-base font-medium transition-colors disabled:cursor-not-allowed ${look} ${className}`}
      {...props}
    />
  )
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`rounded-xl border border-slate-200 bg-white p-6 shadow-sm ${className}`}>{children}</div>
}

export function ErrorText({ error }: { error: unknown }) {
  if (!error) return null
  const message = error instanceof Error ? error.message : 'Algo salió mal.'
  return (
    <p role="alert" className="text-sm text-red-700">
      {message}
    </p>
  )
}
