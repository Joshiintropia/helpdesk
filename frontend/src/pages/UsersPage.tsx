import { useState, type FormEvent } from 'react'
import { useAreas, useCreateUser, useResetPassword, useUpdateUser, useUsers } from '../api/hooks'
import type { Area, AssignableRole, UserAdmin, UserWithPassword } from '../api/types'
import { Button, Card, ErrorText } from '../components/ui'

const roleLabels: Record<AssignableRole, string> = {
  Admin: 'Administrador',
  Supervisor: 'Supervisor (ve y actúa en todas las áreas)',
}

/** Accounts for people without Microsoft 365: the admin creates them and hands over a temporary password. */
export default function UsersPage() {
  const { data: users, isLoading, error } = useUsers()
  const { data: areas } = useAreas(true)
  const [editing, setEditing] = useState<UserAdmin | 'new' | null>(null)
  const [issued, setIssued] = useState<UserWithPassword | null>(null)
  const areaName = (id: number) => areas?.find((a) => a.id === id)?.name ?? `Área ${id}`

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Usuarios</h1>
        <Button onClick={() => { setEditing('new'); setIssued(null) }}>Nuevo usuario</Button>
      </div>

      {issued && <TemporaryPasswordNotice issued={issued} onClose={() => setIssued(null)} />}
      {editing && (
        <UserForm
          user={editing === 'new' ? null : editing}
          areas={areas ?? []}
          onDone={(result) => {
            setEditing(null)
            if (result) setIssued(result)
          }}
        />
      )}

      {isLoading && <p className="text-slate-500">Cargando…</p>}
      <ErrorText error={error} />
      <ul className="space-y-3">
        {users?.map((user) => (
          <li key={user.id} className="flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 bg-white p-4">
            <div className="min-w-60 flex-1">
              <p className="font-medium">
                {user.displayName || user.email}
                {!user.isActive && <span className="ml-2 rounded-full bg-slate-200 px-2 py-0.5 text-xs text-slate-700">Desactivado</span>}
              </p>
              <p className="text-sm text-slate-600">{user.email}</p>
              <p className="text-sm text-slate-500">
                {[
                  ...user.roles.filter((r) => r !== 'Agent').map((r) => (r === 'Admin' ? 'Administrador' : r)),
                  ...user.areaIds.map(areaName),
                ].join(' · ') || 'Solicitante'}
                {user.microsoftLinked && ' · Entra con Microsoft 365'}
                {user.mustChangePassword && ' · Pendiente de cambiar contraseña'}
              </p>
            </div>
            <Button variant="secondary" onClick={() => { setEditing(user); setIssued(null) }}>
              Editar
            </Button>
          </li>
        ))}
      </ul>
    </section>
  )
}

function TemporaryPasswordNotice({ issued, onClose }: { issued: UserWithPassword; onClose: () => void }) {
  return (
    <Card className="border-emerald-300 bg-emerald-50">
      <p className="mb-2 font-medium text-emerald-900">Contraseña temporal para {issued.user.email}</p>
      <p className="mb-3 font-mono text-2xl tracking-wider">{issued.temporaryPassword}</p>
      <p className="mb-4 text-sm text-emerald-900">
        Compártela con la persona por un medio seguro. No se volverá a mostrar; al entrar le pediremos que elija la suya.
      </p>
      <Button variant="secondary" onClick={onClose}>
        Listo
      </Button>
    </Card>
  )
}

function UserForm({ user, areas, onDone }: { user: UserAdmin | null; areas: Area[]; onDone: (issued?: UserWithPassword) => void }) {
  const create = useCreateUser()
  const update = useUpdateUser()
  const reset = useResetPassword()
  const [email, setEmail] = useState(user?.email ?? '')
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [isActive, setIsActive] = useState(user?.isActive ?? true)
  const [roles, setRoles] = useState<AssignableRole[]>(
    (user?.roles.filter((r): r is AssignableRole => r === 'Admin' || r === 'Supervisor')) ?? [],
  )
  const [areaIds, setAreaIds] = useState<number[]>(user?.areaIds ?? [])
  const error = create.error ?? update.error ?? reset.error
  const pending = create.isPending || update.isPending || reset.isPending

  const toggle = <T,>(list: T[], value: T) => (list.includes(value) ? list.filter((v) => v !== value) : [...list, value])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (user) {
      update.mutate({ id: user.id, displayName, isActive, roles, areaIds }, { onSuccess: () => onDone() })
    } else {
      create.mutate({ email, displayName, roles, areaIds }, { onSuccess: (result) => onDone(result) })
    }
  }

  return (
    <Card>
      <form onSubmit={submit} className="space-y-4">
        <h2 className="text-lg font-semibold">{user ? `Editar ${user.displayName || user.email}` : 'Nuevo usuario'}</h2>
        <label className="block">
          <span className="mb-1 block font-medium">Correo</span>
          <input
            type="email"
            required
            disabled={!!user}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="w-full rounded-lg border border-slate-300 px-3 py-2.5 disabled:bg-slate-100"
          />
        </label>
        <label className="block">
          <span className="mb-1 block font-medium">Nombre</span>
          <input required value={displayName} onChange={(e) => setDisplayName(e.target.value)} className="w-full rounded-lg border border-slate-300 px-3 py-2.5" />
        </label>

        <fieldset>
          <legend className="mb-1 font-medium">Áreas que atiende</legend>
          <p className="mb-2 text-sm text-slate-500">Déjalo vacío si solo levantará solicitudes.</p>
          <div className="flex flex-wrap gap-3">
            {areas.map((area) => (
              <label key={area.id} className="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-2">
                <input type="checkbox" checked={areaIds.includes(area.id)} onChange={() => setAreaIds(toggle(areaIds, area.id))} />
                {area.name}
              </label>
            ))}
          </div>
        </fieldset>

        <fieldset>
          <legend className="mb-2 font-medium">Permisos adicionales</legend>
          {(Object.keys(roleLabels) as AssignableRole[]).map((role) => (
            <label key={role} className="flex items-center gap-2">
              <input type="checkbox" checked={roles.includes(role)} onChange={() => setRoles(toggle(roles, role))} />
              {roleLabels[role]}
            </label>
          ))}
        </fieldset>

        {user && (
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
            Activo (si lo desactivas ya no podrá entrar)
          </label>
        )}

        {!user && <p className="text-sm text-slate-500">Al guardar te mostraremos una contraseña temporal para compartirla.</p>}
        <ErrorText error={error} />
        <div className="flex flex-wrap gap-3">
          <Button type="submit" disabled={pending}>
            Guardar
          </Button>
          {user && (
            <Button type="button" variant="secondary" disabled={pending} onClick={() => reset.mutate(user.id, { onSuccess: (result) => onDone(result) })}>
              Generar nueva contraseña temporal
            </Button>
          )}
          <Button type="button" variant="secondary" onClick={() => onDone()}>
            Cancelar
          </Button>
        </div>
      </form>
    </Card>
  )
}
