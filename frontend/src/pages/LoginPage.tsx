import { useState, type FormEvent } from 'react'
import { Navigate, useSearchParams } from 'react-router'
import { useAuthProviders, useLogin, useMe } from '../api/hooks'
import { Button, Card, ErrorText } from '../components/ui'

// Reasons the Microsoft 365 flow sends people back here with ?error=...
const externalErrors: Record<string, string> = {
  inactivo: 'Tu cuenta está desactivada. Pide ayuda a un administrador.',
  'no-registrado': 'Tu cuenta de Microsoft aún no tiene acceso. Pide a un administrador que te dé de alta.',
  externo: 'No pudimos entrar con Microsoft. Intenta de nuevo.',
}

export default function LoginPage() {
  const { data: me } = useMe()
  const { data: providers } = useAuthProviders()
  const login = useLogin()
  const [params] = useSearchParams()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  if (me) return <Navigate to="/" replace />

  const externalError = externalErrors[params.get('error') ?? '']
  const submit = (event: FormEvent) => {
    event.preventDefault()
    login.mutate({ email, password })
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <Card className="w-full max-w-sm">
        <h1 className="mb-1 text-2xl font-semibold">Mesa de ayuda</h1>
        <p className="mb-6 text-slate-600">Entra para levantar o seguir tus solicitudes.</p>

        {externalError && <ErrorText error={new Error(externalError)} />}

        {providers?.microsoft && (
          <>
            <a
              href="/api/auth/external/microsoft"
              className="mt-2 flex w-full items-center justify-center gap-3 rounded-lg border border-slate-300 bg-white px-4 py-2.5 font-medium hover:bg-slate-100"
            >
              <MicrosoftLogo />
              Entrar con Microsoft 365
            </a>
            <div className="my-6 flex items-center gap-3 text-sm text-slate-500">
              <span className="h-px flex-1 bg-slate-200" />o con tu correo y contraseña
              <span className="h-px flex-1 bg-slate-200" />
            </div>
          </>
        )}

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
        <p className="mt-4 text-sm text-slate-500">¿No tienes acceso u olvidaste tu contraseña? Pídelo a un administrador.</p>
      </Card>
    </div>
  )
}

function MicrosoftLogo() {
  return (
    <svg width="18" height="18" viewBox="0 0 21 21" aria-hidden="true">
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  )
}
