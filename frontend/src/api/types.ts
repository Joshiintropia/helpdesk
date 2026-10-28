// Mirrors HelpDesk.Api.Endpoints.Contracts.

export type TicketStatus = 'New' | 'InProgress' | 'WaitingOnRequester' | 'Resolved' | 'Closed'
export type TicketPriority = 'Low' | 'Normal' | 'High' | 'Urgent'
export type SegmentStatus = 'Open' | 'Resolved'
export type MessageVisibility = 'Public' | 'InternalAllAreas' | 'InternalOwnArea'
export type SlaState = 'None' | 'OnTrack' | 'AtRisk' | 'Breached' | 'Paused' | 'Met'
export type SlaTarget = 'FirstResponse' | 'Resolution'
export type DayOfWeek = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

/** Where a segment stands against its SLA: which target counts now and when it is due. */
export interface Sla {
  state: SlaState
  target: SlaTarget | null
  dueAt: string | null
}

export interface Area {
  id: number
  name: string
  description: string | null
  acceptsRequests: boolean
}

export interface Me {
  id: string
  email: string
  displayName: string
  /** True while the user still has the temporary password an administrator gave them. */
  mustChangePassword: boolean
  roles: string[]
  areas: Area[]
}

export interface AuthProviders {
  microsoft: boolean
}

export type AssignableRole = 'Admin' | 'Supervisor'

export interface UserAdmin {
  id: string
  email: string
  displayName: string
  isActive: boolean
  mustChangePassword: boolean
  microsoftLinked: boolean
  roles: string[]
  areaIds: number[]
}

export interface UserWithPassword {
  user: UserAdmin
  temporaryPassword: string
}

export interface Person {
  id: string
  displayName: string
}

export interface TicketSummary {
  id: number
  title: string
  status: TicketStatus
  priority: TicketPriority
  currentAreaId: number
  currentAreaName: string
  createdAt: string
  updatedAt: string
}

export interface Segment {
  id: string
  areaId: number
  areaName: string
  parentSegmentId: string | null
  status: SegmentStatus
  escalationReason: string | null
  escalatedBy: Person | null
  assignee: Person | null
  openedAt: string
  resolvedAt: string | null
  resolutionNote: string | null
  firstRespondedAt: string | null
  firstResponseDueAt: string | null
  resolutionDueAt: string | null
  sla: Sla
}

export interface Message {
  id: string
  author: Person
  authorAreaId: number | null
  body: string
  visibility: MessageVisibility
  source: 'Portal' | 'Email' | 'System'
  createdAt: string
}

export interface TicketDetail {
  id: number
  title: string
  description: string
  status: TicketStatus
  priority: TicketPriority
  requester: Person
  currentAreaId: number
  currentAreaName: string
  createdAt: string
  updatedAt: string
  resolvedAt: string | null
  /** Only present for agents: the escalation chain is internal. */
  segments: Segment[] | null
  messages: Message[]
}

export interface QueueItem {
  ticketId: number
  title: string
  status: TicketStatus
  priority: TicketPriority
  segmentId: string
  areaId: number
  assigneeId: string | null
  escalationReason: string | null
  openedAt: string
  ticketCreatedAt: string
  sla: Sla
}

export interface Holiday {
  id: number
  date: string
  name: string
}

export interface BusinessCalendar {
  id: number
  name: string
  timeZone: string
  /** "HH:mm:ss" */
  workdayStart: string
  workdayEnd: string
  workingDays: DayOfWeek[]
  holidays: Holiday[]
}

/** Targets in business minutes; areaId null is the general policy for that priority. */
export interface SlaPolicy {
  id: number
  areaId: number | null
  priority: TicketPriority
  firstResponseMinutes: number
  resolutionMinutes: number
}

export interface SlaSettings {
  calendar: BusinessCalendar
  policies: SlaPolicy[]
}
