import { useState, type FormEvent } from 'react'
import { useChangePassword } from '../api/hooks'
import { Button, Card, ErrorText } from '../components/ui'

/** Shown instead of the app while the user still has the temporary password an administrator gave them. */
export default function ChangePasswordPage() {
  const change = useChangePassword()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const mismatch = confirm.length > 0 && confirm !== newPassword

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (mismatch) return
    change.mutate({ currentPassword, newPassword })
  }

  return (
    <Card className="mx-auto max-w-md">
      <h1 className="mb-1 text-2xl font-semibold">Elige tu contraseña</h1>
      <p className="mb-6 text-slate-600">
        Entraste con una contraseña temporal. Cámbiala por una que solo tú conozcas: al menos 10 caracteres, con
        mayúsculas, minúsculas y un número.
      </p>
      <form onSubmit={submit} className="space-y-4">
        <PasswordField label="Contraseña temporal" value={currentPassword} onChange={setCurrentPassword} autoComplete="current-password" />
        <PasswordField label="Nueva contraseña" value={newPassword} onChange={setNewPassword} autoComplete="new-password" />
        <PasswordField label="Repite la nueva contraseña" value={confirm} onChange={setConfirm} autoComplete="new-password" />
        {mismatch && <ErrorText error={new Error('Las contraseñas no coinciden.')} />}
        <ErrorText error={change.error} />
        <Button type="submit" className="w-full" disabled={change.isPending || mismatch}>
          Guardar contraseña
        </Button>
      </form>
    </Card>
  )
}

function PasswordField(props: { label: string; value: string; onChange: (value: string) => void; autoComplete: string }) {
  return (
    <label className="block">
      <span className="mb-1 block font-medium">{props.label}</span>
      <input
        type="password"
        required
        autoComplete={props.autoComplete}
        value={props.value}
        onChange={(e) => props.onChange(e.target.value)}
        className="w-full rounded-lg border border-slate-300 px-3 py-2.5"
      />
    </label>
  )
}
