import { useState, type FormEvent } from 'react'
import { useAddHoliday, useAreas, useDeleteHoliday, useSaveCalendar, useSavePolicies, useSlaSettings, type SlaPolicyInput } from '../api/hooks'
import type { BusinessCalendar, DayOfWeek, SlaPolicy, TicketPriority } from '../api/types'
import { Button, Card, ErrorText } from '../components/ui'
import { priorities, priorityLabel } from '../format'

const days: { value: DayOfWeek; label: string }[] = [
  { value: 'Monday', label: 'Lun' },
  { value: 'Tuesday', label: 'Mar' },
  { value: 'Wednesday', label: 'Mié' },
  { value: 'Thursday', label: 'Jue' },
  { value: 'Friday', label: 'Vie' },
  { value: 'Saturday', label: 'Sáb' },
  { value: 'Sunday', label: 'Dom' },
]

const input = 'rounded-lg border border-slate-300 px-3 py-2.5'

/** Working hours, holidays and target times. Times count only within working hours. */
export default function SlaPage() {
  const { data: settings, isLoading, error } = useSlaSettings()

  return (
    <section className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Tiempos de atención (SLA)</h1>
        <p className="mt-1 text-slate-600">
          Los tiempos cuentan solo en horario laboral. Se detienen mientras se espera al solicitante y, para un área que escaló,
          mientras la otra área trabaja. Los cambios aplican a los tickets nuevos y a los que cambian de prioridad.
        </p>
      </div>
      {isLoading && <p className="text-slate-500">Cargando…</p>}
      <ErrorText error={error} />
      {settings && (
        <>
          <Policies policies={settings.policies} />
          <CalendarForm key={JSON.stringify(settings.calendar)} calendar={settings.calendar} />
          <Holidays calendar={settings.calendar} />
        </>
      )}
    </section>
  )
}

type Row = { key: string; areaId: number | null; priority: TicketPriority; firstHours: string; resolutionHours: string }

const toRow = (p: SlaPolicy | SlaPolicyInput): Row => ({
  key: `${p.areaId ?? 'general'}-${p.priority}`,
  areaId: p.areaId,
  priority: p.priority,
  firstHours: String(p.firstResponseMinutes / 60),
  resolutionHours: String(p.resolutionMinutes / 60),
})

function Policies({ policies }: { policies: SlaPolicy[] }) {
  const { data: areas } = useAreas(true)
  const save = useSavePolicies()
  const [rows, setRows] = useState<Row[]>(() => {
    // Always show the four general rows, even if one was deleted.
    const general = priorities.map(
      (priority) =>
        policies.find((p) => p.areaId === null && p.priority === priority) ?? {
          areaId: null,
          priority,
          firstResponseMinutes: 240,
          resolutionMinutes: 1620,
        },
    )
    return [...general, ...policies.filter((p) => p.areaId !== null)].map(toRow)
  })
  const [saved, setSaved] = useState(false)
  const [newArea, setNewArea] = useState<number | ''>('')
  const [newPriority, setNewPriority] = useState<TicketPriority>('Normal')

  const update = (key: string, field: 'firstHours' | 'resolutionHours', value: string) => {
    setSaved(false)
    setRows((current) => current.map((r) => (r.key === key ? { ...r, [field]: value } : r)))
  }

  const addOverride = () => {
    if (newArea === '' || rows.some((r) => r.areaId === newArea && r.priority === newPriority)) return
    const general = rows.find((r) => r.areaId === null && r.priority === newPriority)!
    setRows([...rows, { ...general, key: `${newArea}-${newPriority}`, areaId: newArea }])
    setSaved(false)
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const payload: SlaPolicyInput[] = rows.map((r) => ({
      areaId: r.areaId,
      priority: r.priority,
      firstResponseMinutes: Math.round(Number(r.firstHours) * 60),
      resolutionMinutes: Math.round(Number(r.resolutionHours) * 60),
    }))
    save.mutate(payload, { onSuccess: () => setSaved(true) })
  }

  const areaName = (id: number | null) => (id === null ? 'Todas las áreas' : (areas?.find((a) => a.id === id)?.name ?? `Área ${id}`))

  return (
    <Card>
      <form onSubmit={submit} className="space-y-4">
        <h2 className="text-lg font-semibold">Tiempos por prioridad</h2>
        <p className="text-sm text-slate-600">En horas hábiles. Puedes dar a un área tiempos distintos a los generales.</p>
        <div className="overflow-x-auto">
          <table className="w-full text-left">
            <thead className="text-sm text-slate-500">
              <tr>
                <th className="py-2 pr-3 font-medium">Área</th>
                <th className="py-2 pr-3 font-medium">Prioridad</th>
                <th className="py-2 pr-3 font-medium">Primera respuesta (h)</th>
                <th className="py-2 pr-3 font-medium">Resolución (h)</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.key} className="border-t border-slate-100">
                  <td className="py-2 pr-3">{areaName(r.areaId)}</td>
                  <td className="py-2 pr-3">{priorityLabel(r.priority)}</td>
                  <td className="py-2 pr-3">
                    <input
                      type="number"
                      min="0.25"
                      step="0.25"
                      required
                      aria-label={`Primera respuesta ${areaName(r.areaId)} ${priorityLabel(r.priority)}`}
                      value={r.firstHours}
                      onChange={(e) => update(r.key, 'firstHours', e.target.value)}
                      className={`${input} w-28`}
                    />
                  </td>
                  <td className="py-2 pr-3">
                    <input
                      type="number"
                      min="0.25"
                      step="0.25"
                      required
                      aria-label={`Resolución ${areaName(r.areaId)} ${priorityLabel(r.priority)}`}
                      value={r.resolutionHours}
                      onChange={(e) => update(r.key, 'resolutionHours', e.target.value)}
                      className={`${input} w-28`}
                    />
                  </td>
                  <td className="py-2">
                    {r.areaId !== null && (
                      <button
                        type="button"
                        className="text-sm text-slate-600 underline"
                        onClick={() => {
                          setRows(rows.filter((x) => x.key !== r.key))
                          setSaved(false)
                        }}
                      >
                        Quitar
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="flex flex-wrap items-center gap-3 rounded-lg bg-slate-50 p-3">
          <span className="font-medium">Tiempos propios para</span>
          <select value={newArea} onChange={(e) => setNewArea(e.target.value ? Number(e.target.value) : '')} className={input}>
            <option value="">Elige un área…</option>
            {areas?.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
          <select value={newPriority} onChange={(e) => setNewPriority(e.target.value as TicketPriority)} className={input}>
            {priorities.map((p) => (
              <option key={p} value={p}>
                {priorityLabel(p)}
              </option>
            ))}
          </select>
          <Button type="button" variant="secondary" onClick={addOverride} disabled={newArea === ''}>
            Agregar
          </Button>
        </div>
        <ErrorText error={save.error} />
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={save.isPending}>
            Guardar tiempos
          </Button>
          {saved && <span className="text-emerald-700">Guardado</span>}
        </div>
      </form>
    </Card>
  )
}

function CalendarForm({ calendar }: { calendar: BusinessCalendar }) {
  const save = useSaveCalendar()
  const [timeZone, setTimeZone] = useState(calendar.timeZone)
  const [start, setStart] = useState(calendar.workdayStart.slice(0, 5))
  const [end, setEnd] = useState(calendar.workdayEnd.slice(0, 5))
  const [workingDays, setWorkingDays] = useState<DayOfWeek[]>(calendar.workingDays)

  const toggle = (day: DayOfWeek) =>
    setWorkingDays((current) => (current.includes(day) ? current.filter((d) => d !== day) : [...current, day]))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate({ timeZone, workdayStart: `${start}:00`, workdayEnd: `${end}:00`, workingDays })
  }

  return (
    <Card>
      <form onSubmit={submit} className="space-y-4">
        <h2 className="text-lg font-semibold">Horario laboral</h2>
        <div className="flex flex-wrap gap-4">
          <label className="block">
            <span className="mb-1 block font-medium">Entrada</span>
            <input type="time" required value={start} onChange={(e) => setStart(e.target.value)} className={input} />
          </label>
          <label className="block">
            <span className="mb-1 block font-medium">Salida</span>
            <input type="time" required value={end} onChange={(e) => setEnd(e.target.value)} className={input} />
          </label>
          <label className="block">
            <span className="mb-1 block font-medium">Zona horaria</span>
            <input required value={timeZone} onChange={(e) => setTimeZone(e.target.value)} className={`${input} w-64`} />
          </label>
        </div>
        <fieldset>
          <legend className="mb-1 font-medium">Días laborales</legend>
          <div className="flex flex-wrap gap-2">
            {days.map((d) => (
              <label
                key={d.value}
                className={`cursor-pointer rounded-lg border px-3 py-2 ${workingDays.includes(d.value) ? 'border-brand-600 bg-brand-50 text-brand-700' : 'border-slate-300'}`}
              >
                <input type="checkbox" className="sr-only" checked={workingDays.includes(d.value)} onChange={() => toggle(d.value)} />
                {d.label}
              </label>
            ))}
          </div>
        </fieldset>
        <ErrorText error={save.error} />
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={save.isPending}>
            Guardar horario
          </Button>
          {save.isSuccess && <span className="text-emerald-700">Guardado</span>}
        </div>
      </form>
    </Card>
  )
}

function Holidays({ calendar }: { calendar: BusinessCalendar }) {
  const add = useAddHoliday()
  const remove = useDeleteHoliday()
  const [date, setDate] = useState('')
  const [name, setName] = useState('')

  const submit = (event: FormEvent) => {
    event.preventDefault()
    add.mutate({ date, name }, { onSuccess: () => { setDate(''); setName('') } })
  }

  return (
    <Card>
      <h2 className="mb-4 text-lg font-semibold">Días festivos</h2>
      <p className="mb-4 text-sm text-slate-600">Estos días no cuentan para los tiempos de atención.</p>
      {calendar.holidays.length === 0 && <p className="mb-4 text-slate-500">No hay días festivos registrados.</p>}
      <ul className="mb-4 space-y-2">
        {calendar.holidays.map((h) => (
          <li key={h.id} className="flex items-center gap-3">
            <span className="w-32 text-slate-600">
              {new Intl.DateTimeFormat('es-MX', { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(h.date))}
            </span>
            <span className="flex-1">{h.name}</span>
            <button className="text-sm text-slate-600 underline" disabled={remove.isPending} onClick={() => remove.mutate(h.id)}>
              Quitar
            </button>
          </li>
        ))}
      </ul>
      <form onSubmit={submit} className="flex flex-wrap gap-3">
        <input type="date" required value={date} onChange={(e) => setDate(e.target.value)} className={input} />
        <input
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Ejemplo: Día de la Revolución"
          className={`${input} min-w-60 flex-1`}
        />
        <Button type="submit" variant="secondary" disabled={add.isPending}>
          Agregar
        </Button>
      </form>
      <ErrorText error={add.error ?? remove.error} />
    </Card>
  )
}
