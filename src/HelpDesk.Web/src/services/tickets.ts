import type { Role } from './session'

export const categories: Record<number, string> = {
  1: 'Hardware',
  2: 'Software',
  3: 'Network',
  4: 'Account',
  5: 'Other',
}
export const priorities: Record<number, string> = {
  1: 'Low',
  2: 'Medium',
  3: 'High',
  4: 'Critical',
}
export const statuses: Record<string, string> = {
  OPEN: 'Open',
  IN_PROGRESS: 'In Progress',
  RESOLVED: 'Resolved',
  CLOSED: 'Closed',
}
export const canManageTickets = (role: Role | undefined) =>
  role === 'SYSTEM_ADMIN' || role === 'SUPPORT_ADMIN'
export const formatTicketDate = (value: string) => new Date(value).toLocaleString()

export interface TicketItem {
  id: number
  ticketNumber: string
  title: string
  category: string
  priority: string
  status: string
  requestedByUserId: number
  requesterName: string
  assignedToUserId: number | null
  assigneeName: string | null
  createdAtUtc: string
  updatedAtUtc: string
}
export interface TicketPage {
  items: TicketItem[]
  total: number
  page: number
}
export interface AttachmentItem {
  id: number
  fileName: string
  contentType: string
  sizeBytes: number
}
export const attachmentAccept =
  '.png,.jpg,.jpeg,.gif,.webp,.bmp,.pdf,.doc,.docx,.xls,.xlsx,.txt,.log'
export const fileSize = (bytes: number) =>
  bytes < 1024 * 1024
    ? `${Math.max(1, Math.ceil(bytes / 1024))} KB`
    : `${(bytes / (1024 * 1024)).toFixed(1)} MB`
export interface WorkflowState {
  id: number
  ticketId: number
  templateCode: string
  templateVersion: number
  currentStepCode: string
  status: string
  rowVersion: string
  availableActions: { code: string; name: string; requiresComment: boolean }[]
  history: {
    fromStepCode: string | null
    toStepCode: string
    actionCode: string
    actorUserId: number
    actorName: string
    comment: string | null
    actionAtUtc: string
  }[]
}
export interface TicketDetail {
  ticket: TicketItem
  description: string
  rowVersion: string
  workflow: WorkflowState
  managementHistory: {
    id: number
    eventType: string
    actorName: string
    oldAssigneeName: string | null
    newAssigneeName: string | null
    oldPriority: string | null
    newPriority: string | null
    createdAtUtc: string
    delegationId: number | null
    comment: string | null
  }[]
}
