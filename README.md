# Mesa de ayuda

Mesa de ayuda open source pensada para que **cualquier persona, aunque no sea técnica, levante una solicitud en menos de un minuto**, y para que las áreas que la atienden puedan pasársela entre ellas sin duplicar tickets.

- **Para quien pide ayuda:** elige con quién necesita ayuda, cuenta qué pasa con sus palabras y sigue su solicitud en lenguaje claro (Nueva, En atención, Esperando tu respuesta, Resuelta, Cerrada).
- **Para quien atiende:** una bandeja por área, notas internas y **escalamiento en cadena**: un mismo ticket puede pasar de Customer Happiness a Finanzas, de Finanzas a TI y de TI a Desarrollo. Cada área resuelve **su parte** (el ticket regresa a quien lo escaló) o **todo** (se cierra para toda la cadena).
- **Para quien instala:** un solo `docker compose up`, una base PostgreSQL y nada más (sin Redis ni Elasticsearch).

## Stack

| Capa | Tecnología |
| --- | --- |
| API | ASP.NET Core 10 (minimal APIs), ASP.NET Core Identity |
| Datos | PostgreSQL 17, EF Core 10 + Npgsql, esquema en snake_case |
| Web | React 19 + TypeScript + Vite, Tailwind CSS 4, TanStack Query |
| Pruebas | xUnit (dominio y API contra PostgreSQL real) |
| Despliegue | Docker Compose; GitHub Actions para CI |

## Instalar con Docker

```bash
cp .env.example .env      # cambia contraseñas y el correo del administrador
docker compose up -d --build
```

Abre `http://localhost:8080` y entra con `ADMIN_EMAIL` / `ADMIN_PASSWORD`. Las migraciones se aplican solas al arrancar.

## Acceso

Hay dos formas de entrar y pueden convivir:

- **Cuentas creadas por un administrador** (siempre disponibles). En *Usuarios* el administrador da de alta a la persona con su correo, le asigna áreas y permisos, y recibe una contraseña temporal para compartirla. Al entrar por primera vez, la persona elige su propia contraseña. Desde ahí mismo se puede desactivar una cuenta o generar otra contraseña temporal.
- **Microsoft 365** (opcional). Con `MICROSOFT_LOGIN_ENABLED=true` aparece el botón "Entrar con Microsoft 365". Si ya existe un usuario con ese correo, se vincula a su cuenta y conserva sus áreas y permisos. Si no existe, se crea como solicitante (`MICROSOFT_AUTO_PROVISION=true`) o se le niega el acceso hasta que un administrador lo dé de alta (`false`).

Para Microsoft 365, registra una aplicación en Entra ID (cuenta de tu organización únicamente) con la URL de respuesta `https://TU-DOMINIO/api/auth/signin-microsoft` y un secreto de cliente, y llena las variables `MICROSOFT_*` de `.env`. El tenant debe ser el de tu organización; `common` y `organizations` se rechazan porque las cuentas se emparejan por correo.

## Correo

El buzón de ayuda (por ejemplo `ayuda@tuempresa.com`) puede crear tickets y la aplicación avisa al solicitante por correo. Leer y enviar se configuran por separado en `.env`:

- **Leer el buzón** (`EMAIL_INBOUND_PROVIDER`): cada correo nuevo abre un ticket en el área de `EMAIL_DEFAULT_AREA_ID` (o la primera que acepta solicitudes). Si el asunto trae el folio `[#123]` y quien escribe es el solicitante o alguien de un área del ticket, se agrega como mensaje a ese ticket, sin la parte citada; si estaba resuelto, se reabre. Se ignoran respuestas automáticas, correos del propio buzón y duplicados.
- **Enviar avisos** (`EMAIL_OUTBOUND_PROVIDER`): "Recibimos tu solicitud", cada respuesta pública de un agente y "Tu solicitud quedó resuelta". Las notas internas nunca salen. Los avisos se guardan primero en la base (`email_notifications`) y se envían en segundo plano con reintentos, así que una caída del servidor de correo no frena a nadie.

Con **Microsoft 365** usa `Graph` en ambos: registra una app en Entra ID con permisos de *aplicación* `Mail.ReadWrite` y `Mail.Send`, da consentimiento de administrador y llena `GRAPH_*`. Conviene limitar la app solo al buzón de ayuda con una [Application Access Policy](https://learn.microsoft.com/graph/auth-limit-mailbox-access) o RBAC de Exchange. Sin Microsoft 365, usa `Imap` y `Smtp` con cualquier servidor.

Como en cualquier mesa de ayuda por correo, el remitente de un correo se puede falsificar: usa SPF, DKIM y DMARC en tu dominio, y deja `EMAIL_CREATE_UNKNOWN_SENDERS=false` si solo deben escribir personas que ya tienen cuenta.

## Desarrollo local

Requisitos: .NET SDK 10, Node 22 y un PostgreSQL local con un usuario `helpdesk` / `helpdesk` que pueda crear bases de datos.

```bash
# API en http://localhost:5080 (crea la base, aplica migraciones y un admin de prueba)
cd backend
dotnet run --project src/HelpDesk.Api

# Web en http://localhost:5173 (redirige /api a la API)
cd frontend
npm install
npm run dev
```

En desarrollo el administrador es `admin@helpdesk.local` / `CambiaEsto123` y se permite el auto-registro (`/api/auth/register`). En producción el auto-registro está apagado (`Auth:AllowSelfRegistration`).

### Pruebas

```bash
cd backend && dotnet test      # usa HELPDESK_TEST_DB o el PostgreSQL local; crea y borra una base temporal
cd frontend && npm run lint && npm run build
```

### Migraciones

```bash
cd backend
dotnet tool restore
dotnet ef migrations add NombreDelCambio -p src/HelpDesk.Infrastructure -s src/HelpDesk.Api -o Persistence/Migrations
```

## Estructura

```
backend/
  src/HelpDesk.Domain          Reglas del negocio: Ticket, tramos (TicketSegment), mensajes, eventos, áreas, SLA
  src/HelpDesk.Infrastructure  EF Core, Identity, migraciones e inicialización de la base
  src/HelpDesk.Api             Endpoints HTTP, autenticación por cookie y permisos por área
  tests/HelpDesk.Tests         Pruebas del dominio y de la API
frontend/                      Portal del solicitante y bandeja de agentes
```

## Modelo en una línea por tabla

- `tickets`: la solicitud; su `id` es el folio.
- `ticket_segments`: los tramos; uno por área que trabaja en el ticket, enlazados con `parent_segment_id`.
- `ticket_messages`: la conversación, con notas internas para todas las áreas o solo para la propia.
- `ticket_events`: bitácora de cada cambio; de aquí salen las métricas y la auditoría.
- `processed_emails`: cada correo leído del buzón y qué se hizo con él.
- `email_notifications`: avisos por enviar y enviados, con sus errores.
- `areas`, `area_members`, `categories`, `sla_policies`, `business_calendars`, `holidays`, y las tablas de usuarios y roles.

## Próximos pasos

1. Cálculo de SLA por tramo con horario laboral y días festivos.
2. Dashboard de métricas.
3. Adjuntos, encuesta de satisfacción y cierre automático de tickets resueltos.

## Licencia

[GNU AGPL v3](LICENSE). Puedes usarlo, modificarlo e instalarlo en tu empresa; si ofreces una versión modificada como servicio, debes publicar tus cambios.
