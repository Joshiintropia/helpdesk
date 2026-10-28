import { useState } from 'react'
import { useAreas, useMe, useMetrics } from '../api/hooks'
import type { AreaMetrics, Backlog, Metrics, SlaCompliance } from '../api/types'
import { Card, ErrorText } from '../components/ui'
import { formatMinutes } from '../format'

const periods = [
  { days: 7, label: 'Últimos 7 días' },
  { days: 30, label: 'Últimos 30 días' },
  { days: 90, label: 'Últimos 90 días' },
]

// Reserved status colors (never reused as series colors); each always ships with a text label.
const backlogParts: { key: keyof Backlog; label: string; color: string }[] = [
  { key: 'breached', label: 'Vencidos', color: '#d03b3b' },
  { key: 'atRisk', label: 'Por vencer', color: '#fab219' },
  { key: 'onTrack', label: 'A tiempo', color: '#0ca30c' },
  { key: 'paused', label: 'En pausa', color: '#c3c2b7' },
  { key: 'noSla', label: 'Sin SLA', color: '#e1e0d9' },
]

const select = 'rounded-lg border border-slate-300 bg-white px-3 py-2.5'

/** Volume, SLA compliance and time per area, and where tickets travel. Agents see their areas; supervisors see all. */
export default function DashboardPage() {
  const { data: me } = useMe()
  const canSeeAll = me?.roles.some((r) => r === 'Admin' || r === 'Supervisor') ?? false
  const { data: allAreas } = useAreas(true)
  const areas = canSeeAll ? (allAreas ?? []) : (me?.areas ?? [])
  const [days, setDays] = useState(30)
  const [areaId, setAreaId] = useState<number | null>(null)
  const { data, error, isFetching, isLoading } = useMetrics(days, areaId)

  return (
    <section className="space-y-6">
      <h1 className="text-2xl font-semibold">Métricas</h1>

      <div className="flex flex-wrap gap-3">
        <select aria-label="Periodo" value={days} onChange={(e) => setDays(Number(e.target.value))} className={select}>
          {periods.map((p) => (
            <option key={p.days} value={p.days}>
              {p.label}
            </option>
          ))}
        </select>
        <select
          aria-label="Área"
          value={areaId ?? ''}
          onChange={(e) => setAreaId(e.target.value ? Number(e.target.value) : null)}
          className={select}
        >
          <option value="">{canSeeAll ? 'Todas las áreas' : 'Mis áreas'}</option>
          {areas.map((a) => (
            <option key={a.id} value={a.id}>
              {a.name}
            </option>
          ))}
        </select>
      </div>

      {isLoading && <p className="text-slate-500">Cargando…</p>}
      <ErrorText error={error} />
      {data && (
        <div className={`space-y-6 transition-opacity ${isFetching ? 'opacity-60' : ''}`}>
          <Kpis metrics={data} />
          <DailyChart daily={data.daily} />
          <AreasTable areas={data.areas} />
          <Escalations escalations={data.escalations} />
          <p className="text-sm text-slate-500">
            Los tiempos están en horas hábiles ({data.timeZone}) y no cuentan las pausas mientras se espera al solicitante.
          </p>
        </div>
      )}
    </section>
  )
}

function pct(met: number, total: number) {
  return total === 0 ? null : Math.round((met / total) * 100)
}

function Tile({ label, value, detail, alert }: { label: string; value: string; detail?: string; alert?: string }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      <p className="text-sm text-slate-600">{label}</p>
      <p className="mt-1 text-3xl font-semibold text-slate-900">{value}</p>
      {detail && <p className="mt-1 text-sm text-slate-500">{detail}</p>}
      {alert && (
        <p className="mt-1 flex items-center gap-1 text-sm font-medium text-red-700">
          <span aria-hidden="true">⚠</span>
          {alert}
        </p>
      )}
    </div>
  )
}

function slaTile(met: number, total: number) {
  const value = pct(met, total)
  return { value: value === null ? '—' : `${value}%`, detail: total === 0 ? 'Sin casos en el periodo' : `${met} de ${total} a tiempo` }
}

function Kpis({ metrics }: { metrics: Metrics }) {
  const { totals } = metrics
  const first = slaTile(totals.sla.firstResponseMet, totals.sla.firstResponseTotal)
  const resolution = slaTile(totals.sla.resolutionMet, totals.sla.resolutionTotal)
  return (
    <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
      <Tile label="Solicitudes recibidas" value={String(totals.received)} />
      <Tile label="Resueltas" value={String(totals.resolved)} />
      <Tile
        label="Abiertas ahora"
        value={String(totals.open)}
        alert={totals.overdue > 0 ? `${totals.overdue} con SLA vencido` : undefined}
      />
      <Tile label="Primera respuesta a tiempo" value={first.value} detail={first.detail} />
      <Tile label="Resolución a tiempo" value={resolution.value} detail={resolution.detail} />
      <Tile
        label="Tiempo típico de resolución"
        value={totals.sla.medianResolutionMinutes === null ? '—' : formatMinutes(totals.sla.medianResolutionMinutes)}
        detail={
          totals.sla.medianFirstResponseMinutes === null
            ? undefined
            : `Primera respuesta: ${formatMinutes(totals.sla.medianFirstResponseMinutes)}`
        }
      />
    </div>
  )
}

const dayFormat = new Intl.DateTimeFormat('es-MX', { day: 'numeric', month: 'short', timeZone: 'UTC' })
const formatDay = (iso: string) => dayFormat.format(new Date(iso))

/** One series, so no legend: the title names it. Bars carry their own hover/focus tooltip. */
function DailyChart({ daily }: { daily: Metrics['daily'] }) {
  const [hover, setHover] = useState<number | null>(null)
  const width = 720
  const height = 220
  const pad = { top: 12, right: 8, bottom: 28, left: 32 }
  const plotW = width - pad.left - pad.right
  const plotH = height - pad.top - pad.bottom
  const max = Math.max(1, ...daily.map((d) => d.received))
  const step = niceStep(max)
  const top = Math.ceil(max / step) * step
  const ticks = Array.from({ length: top / step + 1 }, (_, i) => i * step)
  const band = plotW / daily.length
  const barW = Math.min(24, Math.max(2, band - 2))
  const y = (v: number) => pad.top + plotH - (v / top) * plotH
  const labelEvery = Math.ceil(daily.length / 8)
  const active = hover === null ? null : daily[hover]

  return (
    <Card>
      <h2 className="mb-4 text-lg font-semibold">Solicitudes recibidas por día</h2>
      <div className="relative overflow-x-auto">
        <div className="relative min-w-[560px]">
        <svg viewBox={`0 0 ${width} ${height}`} className="h-auto w-full" role="img" aria-label="Solicitudes recibidas por día">
          {ticks.map((t) => (
            <g key={t}>
              <line x1={pad.left} x2={width - pad.right} y1={y(t)} y2={y(t)} stroke={t === 0 ? '#c3c2b7' : '#e1e0d9'} strokeWidth={1} />
              <text x={pad.left - 6} y={y(t)} dy="0.32em" textAnchor="end" fontSize={11} fill="#898781" className="tabular-nums">
                {t}
              </text>
            </g>
          ))}
          {daily.map((d, i) => {
            const x = pad.left + i * band + (band - barW) / 2
            const h = y(0) - y(d.received)
            return (
              <g key={d.date}>
                {h > 0 && (
                  <path
                    d={roundedTop(x, y(d.received), barW, h, Math.min(4, barW / 2, h))}
                    fill={hover === i ? '#5598e7' : '#2a78d6'}
                  />
                )}
                {/* The hit area is the whole band, taller than the bar. */}
                <rect
                  x={pad.left + i * band}
                  y={pad.top}
                  width={band}
                  height={plotH}
                  fill="transparent"
                  tabIndex={0}
                  aria-label={`${formatDay(d.date)}: ${d.received} recibidas`}
                  onPointerEnter={() => setHover(i)}
                  onPointerLeave={() => setHover(null)}
                  onFocus={() => setHover(i)}
                  onBlur={() => setHover(null)}
                />
                {i % labelEvery === 0 && (
                  <text x={pad.left + i * band + band / 2} y={height - 8} textAnchor="middle" fontSize={11} fill="#898781">
                    {formatDay(d.date)}
                  </text>
                )}
              </g>
            )
          })}
        </svg>
        {active && hover !== null && (
          <div
            className="pointer-events-none absolute top-0 whitespace-nowrap rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm shadow"
            style={tooltipPosition((pad.left + hover * band + band / 2) / width)}
          >
            <p className="font-semibold text-slate-900">{active.received} recibidas</p>
            <p className="text-slate-600">{active.resolved} resueltas</p>
            <p className="text-slate-500">{formatDay(active.date)}</p>
          </div>
        )}
        </div>
      </div>
      <details className="mt-3 text-sm">
        <summary className="cursor-pointer text-slate-600">Ver como tabla</summary>
        <table className="mt-2 w-full max-w-sm tabular-nums">
          <thead className="text-slate-500">
            <tr>
              <th className="py-1 text-left font-medium">Día</th>
              <th className="py-1 text-right font-medium">Recibidas</th>
              <th className="py-1 text-right font-medium">Resueltas</th>
            </tr>
          </thead>
          <tbody>
            {daily.map((d) => (
              <tr key={d.date} className="border-t border-slate-100">
                <td className="py-1">{formatDay(d.date)}</td>
                <td className="py-1 text-right">{d.received}</td>
                <td className="py-1 text-right">{d.resolved}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </Card>
  )
}

/** Centered over the bar, but kept inside the card near either edge. */
function tooltipPosition(fraction: number) {
  if (fraction > 0.8) return { right: `${(1 - fraction) * 100}%`, marginRight: '-12px' }
  if (fraction < 0.2) return { left: `${fraction * 100}%`, marginLeft: '-12px' }
  return { left: `${fraction * 100}%`, transform: 'translateX(-50%)' }
}

function niceStep(max: number) {
  const raw = max / 4
  const magnitude = 10 ** Math.floor(Math.log10(Math.max(1, raw)))
  const step = [1, 2, 5, 10].map((m) => m * magnitude).find((s) => s >= raw) ?? magnitude * 10
  return Math.max(1, step)
}

/** A bar with a 4px rounded data end and a square end on the baseline. */
function roundedTop(x: number, y: number, w: number, h: number, r: number) {
  return `M${x},${y + h} V${y + r} Q${x},${y} ${x + r},${y} H${x + w - r} Q${x + w},${y} ${x + w},${y + r} V${y + h} Z`
}

function compliance(sla: SlaCompliance, kind: 'first' | 'resolution') {
  const [met, total] = kind === 'first' ? [sla.firstResponseMet, sla.firstResponseTotal] : [sla.resolutionMet, sla.resolutionTotal]
  const value = pct(met, total)
  if (value === null) return <span className="text-slate-400">—</span>
  return (
    <span className={value < 80 ? 'font-medium text-red-700' : ''} title={`${met} de ${total} a tiempo`}>
      {value}%
    </span>
  )
}

function AreasTable({ areas }: { areas: AreaMetrics[] }) {
  const maxOpen = Math.max(1, ...areas.map((a) => a.open))
  return (
    <Card>
      <h2 className="mb-1 text-lg font-semibold">Por área</h2>
      <p className="mb-4 text-sm text-slate-600">Cada área se mide en su propio tramo del ticket. Los pendientes se agrupan por cómo van con su SLA.</p>
      {areas.length === 0 ? (
        <p className="text-slate-500">Sin actividad en el periodo.</p>
      ) : (
        <>
          <ul className="mb-4 flex flex-wrap gap-4 text-sm text-slate-600" aria-label="Leyenda">
            {backlogParts.map((p) => (
              <li key={p.key} className="flex items-center gap-1.5">
                <span className="inline-block h-3 w-3 rounded-sm" style={{ background: p.color }} />
                {p.label}
              </li>
            ))}
          </ul>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-left tabular-nums">
              <thead className="text-sm text-slate-500">
                <tr>
                  <th className="py-2 pr-3 font-medium">Área</th>
                  <th className="py-2 pr-3 font-medium">Pendientes</th>
                  <th className="py-2 pr-3 text-right font-medium">Recibidas</th>
                  <th className="py-2 pr-3 text-right font-medium">Resueltas</th>
                  <th className="py-2 pr-3 text-right font-medium">Escaló</th>
                  <th className="py-2 pr-3 text-right font-medium">1.ª respuesta a tiempo</th>
                  <th className="py-2 pr-3 text-right font-medium">Resolución a tiempo</th>
                  <th className="py-2 text-right font-medium">Tiempo típico</th>
                </tr>
              </thead>
              <tbody>
                {areas.map((a) => (
                  <tr key={a.areaId} className="border-t border-slate-100">
                    <td className="py-3 pr-3 font-medium">{a.name}</td>
                    <td className="py-3 pr-3">
                      <BacklogBar backlog={a.backlog} total={a.open} max={maxOpen} />
                    </td>
                    <td className="py-3 pr-3 text-right">{a.received}</td>
                    <td className="py-3 pr-3 text-right">{a.resolved}</td>
                    <td className="py-3 pr-3 text-right">{a.escalatedOut}</td>
                    <td className="py-3 pr-3 text-right">{compliance(a.sla, 'first')}</td>
                    <td className="py-3 pr-3 text-right">{compliance(a.sla, 'resolution')}</td>
                    <td className="py-3 text-right">
                      {a.sla.medianResolutionMinutes === null ? <span className="text-slate-400">—</span> : formatMinutes(a.sla.medianResolutionMinutes)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Card>
  )
}

/** Stacked horizontal bar of an area's open segments, scaled to the busiest area so rows compare. */
function BacklogBar({ backlog, total, max }: { backlog: Backlog; total: number; max: number }) {
  const parts = backlogParts.filter((p) => backlog[p.key] > 0)
  const summary = parts.map((p) => `${backlog[p.key]} ${p.label.toLowerCase()}`).join(', ')
  return (
    <div className="flex items-center gap-2" title={summary || 'Sin pendientes'}>
      <div className="flex h-3 w-40 gap-[2px]" role="img" aria-label={summary || 'Sin pendientes'}>
        {parts.map((p, i) => (
          <div
            key={p.key}
            style={{ width: `${(backlog[p.key] / max) * 100}%`, background: p.color }}
            className={`h-full ${i === parts.length - 1 ? 'rounded-r' : ''}`}
          />
        ))}
      </div>
      <span className="w-6 text-right">{total}</span>
    </div>
  )
}

function Escalations({ escalations }: { escalations: Metrics['escalations'] }) {
  return (
    <Card>
      <h2 className="mb-1 text-lg font-semibold">Escalamientos</h2>
      <p className="mb-4 text-sm text-slate-600">Hacia dónde se mueven los tickets entre áreas.</p>
      {escalations.length === 0 ? (
        <p className="text-slate-500">No hubo escalamientos en el periodo.</p>
      ) : (
        <ul className="space-y-2">
          {escalations.map((e) => (
            <li key={`${e.fromAreaId}-${e.toAreaId}`} className="flex flex-wrap items-center gap-2">
              <span className="font-medium">{e.fromName}</span>
              <span aria-hidden="true" className="text-slate-400">→</span>
              <span className="sr-only">escaló a</span>
              <span className="font-medium">{e.toName}</span>
              <span className="ml-auto tabular-nums text-slate-600">
                {e.count} {e.count === 1 ? 'ticket' : 'tickets'}
              </span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
