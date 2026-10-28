import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from './client'
import type {
  Area,
  AssignableRole,
  AuthProviders,
  DayOfWeek,
  Me,
  QueueItem,
  SlaSettings,
  TicketDetail,
  TicketPriority,
  TicketSummary,
  UserAdmin,
  UserWithPassword,
} from './types'

export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try {
        return await api.get<Me>('/api/me')
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    staleTime: 60_000,
  })
}

export function useLogin() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { email: string; password: string }) => api.post('/api/auth/login?useCookies=true', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['me'] }),
  })
}

export function useLogout() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.post('/api/auth/logout'),
    onSuccess: () => client.clear(),
  })
}

export function useAuthProviders() {
  return useQuery({ queryKey: ['auth', 'providers'], queryFn: () => api.get<AuthProviders>('/api/auth/providers'), staleTime: Infinity })
}

export function useChangePassword() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { currentPassword: string; newPassword: string }) => api.post('/api/account/change-password', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['me'] }),
  })
}

export interface UserInput {
  email?: string
  displayName: string
  isActive?: boolean
  roles: AssignableRole[]
  areaIds: number[]
}

export function useUsers() {
  return useQuery({ queryKey: ['admin', 'users'], queryFn: () => api.get<UserAdmin[]>('/api/admin/users') })
}

export function useCreateUser() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: UserInput) => api.post<UserWithPassword>('/api/admin/users', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['admin', 'users'] }),
  })
}

export function useUpdateUser() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...input }: UserInput & { id: string }) => api.put<UserAdmin>(`/api/admin/users/${id}`, input),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ['admin', 'users'] })
      client.invalidateQueries({ queryKey: ['me'] })
    },
  })
}

export function useResetPassword() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<UserWithPassword>(`/api/admin/users/${id}/reset-password`),
    onSuccess: () => client.invalidateQueries({ queryKey: ['admin', 'users'] }),
  })
}

export interface AreaInput {
  name: string
  description: string | null
  acceptsRequests: boolean
}

export function useSaveArea() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...input }: AreaInput & { id?: number }) =>
      id ? api.put<Area>(`/api/areas/${id}`, input) : api.post<Area>('/api/areas', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['areas'] }),
  })
}

export function useAreas(all = false) {
  return useQuery({
    queryKey: ['areas', all],
    queryFn: () => api.get<Area[]>(`/api/areas${all ? '?all=true' : ''}`),
  })
}

export function useMyTickets() {
  return useQuery({ queryKey: ['tickets', 'mine'], queryFn: () => api.get<TicketSummary[]>('/api/tickets/mine') })
}

export function useQueue() {
  return useQuery({
    queryKey: ['tickets', 'queue'],
    queryFn: () => api.get<QueueItem[]>('/api/tickets/queue'),
    refetchInterval: 30_000,
  })
}

export function useTicket(id: number) {
  return useQuery({ queryKey: ['tickets', id], queryFn: () => api.get<TicketDetail>(`/api/tickets/${id}`) })
}

export function useCreateTicket() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { areaId: number; description: string }) => api.post<TicketDetail>('/api/tickets', input),
    onSuccess: () => client.invalidateQueries({ queryKey: ['tickets'] }),
  })
}

/** Any action on one ticket: the API answers with the updated ticket, which replaces the cached one. */
export function useTicketAction(id: number) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ path, body }: { path: string; body?: unknown }) => api.post<TicketDetail>(`/api/tickets/${id}${path}`, body),
    onSuccess: (ticket) => {
      client.setQueryData(['tickets', id], ticket)
      client.invalidateQueries({ queryKey: ['tickets', 'mine'] })
      client.invalidateQueries({ queryKey: ['tickets', 'queue'] })
    },
  })
}

export function useSlaSettings() {
  return useQuery({ queryKey: ['sla'], queryFn: () => api.get<SlaSettings>('/api/sla') })
}

export interface CalendarInput {
  timeZone: string
  workdayStart: string
  workdayEnd: string
  workingDays: DayOfWeek[]
}

export interface SlaPolicyInput {
  areaId: number | null
  priority: TicketPriority
  firstResponseMinutes: number
  resolutionMinutes: number
}

/** Every SLA change answers with the full settings, which replace the cached ones. */
function useSlaMutation<TInput>(send: (input: TInput) => Promise<SlaSettings>) {
  const client = useQueryClient()
  return useMutation({ mutationFn: send, onSuccess: (settings) => client.setQueryData(['sla'], settings) })
}

export const useSaveCalendar = () => useSlaMutation((input: CalendarInput) => api.put<SlaSettings>('/api/sla/calendar', input))

export const useAddHoliday = () =>
  useSlaMutation((input: { date: string; name: string }) => api.post<SlaSettings>('/api/sla/calendar/holidays', input))

export const useDeleteHoliday = () => useSlaMutation((id: number) => api.delete<SlaSettings>(`/api/sla/calendar/holidays/${id}`))

export const useSavePolicies = () => useSlaMutation((input: SlaPolicyInput[]) => api.put<SlaSettings>('/api/sla/policies', input))
