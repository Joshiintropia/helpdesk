import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router'
import { useLogin, useMe } from '../api/hooks'
import { Button, Card, ErrorText } from '../components/ui'

export default function LoginPage() {
  const { data: me } = useMe()
  const login = useLogin()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  if (me) return <Navigate to="/" replace />

  const submit = (event: FormEvent) => {
    event.preventDefault()
    login.mutate({ email, password })
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <Card className="w-full max-w-sm">
        <h1 className="mb-1 text-2xl font-semibold">Mesa de ayuda</h1>
        <p className="mb-6 text-slate-600">Entra con tu correo de la empresa.</p>
        <form onSubmit={submit} className="space-y-4">
          <label className="block">
            <span className="mb-1 block font-medium">Correo</span>
            <input
              type="email"
              required
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full rounded-lg border border-slate-300 px-3 py-2.5"
            />
          </label>
          <label className="block">
            <span className="mb-1 block font-medium">Contraseña</span>
            <input
              type="password"
              required
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-lg border border-slate-300 px-3 py-2.5"
            />
          </label>
          {login.isError && <ErrorText error={new Error('Correo o contraseña incorrectos.')} />}
          <Button type="submit" className="w-full" disabled={login.isPending}>
            {login.isPending ? 'Entrando…' : 'Entrar'}
          </Button>
        </form>
      </Card>
    </div>
  )
}
