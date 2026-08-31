import { Injectable, signal } from '@angular/core';

export interface WorkflowLeadContext {
  id: string;
  name?: string;
  mobile?: string;
  email?: string;
  campaign?: string;
  owner?: string;
  language?: string;
  source?: string;
  stage?: string;
}

export interface WorkflowFollowUpContext {
  id: string;
  leadId: string;
  recordName?: string;
  scheduledDate?: string;
  scheduledTime?: string;
  priority?: string;
  purpose?: string;
  status?: string;
  assignedTo?: string;
}

@Injectable({ providedIn: 'root' })
export class DonorLeadWorkflowState {
  private readonly leadKey = 'ydot.crm.selectedLead';
  private readonly followUpKey = 'ydot.crm.selectedFollowUp';
  private readonly createdLeadsKey = 'ydot.crm.createdLeads';
  private readonly followUpsKey = 'ydot.crm.followUps';
  private readonly communicationsKey = 'ydot.crm.communications';

  readonly selectedLead = signal<WorkflowLeadContext | null>(this.read<WorkflowLeadContext>(this.leadKey));
  readonly selectedFollowUp = signal<WorkflowFollowUpContext | null>(this.read<WorkflowFollowUpContext>(this.followUpKey));

  selectLead(lead: WorkflowLeadContext): void {
    this.selectedLead.set(lead);
    this.write(this.leadKey, lead);
  }

  selectFollowUp(followUp: WorkflowFollowUpContext): void {
    this.selectedFollowUp.set(followUp);
    this.write(this.followUpKey, followUp);
  }

  addCreatedLead(lead: WorkflowLeadContext & Record<string, unknown>): void {
    const items = this.readArray<Record<string, unknown>>(this.createdLeadsKey);
    this.write(this.createdLeadsKey, [lead, ...items.filter((x) => x['id'] !== lead.id)]);
    this.selectLead(lead);
  }

  saveFollowUp(followUp: WorkflowFollowUpContext & Record<string, unknown>): void {
    const items = this.readArray<Record<string, unknown>>(this.followUpsKey);
    this.write(this.followUpsKey, [followUp, ...items.filter((x) => x['id'] !== followUp.id)]);
    this.selectFollowUp(followUp);
  }

  updateFollowUp(id: string, patch: Record<string, unknown>): void {
    const items = this.readArray<Record<string, unknown>>(this.followUpsKey);
    const next = items.map((x) => x['id'] === id ? { ...x, ...patch } : x);
    this.write(this.followUpsKey, next);
  }

  addCommunication(item: Record<string, unknown>): void {
    const items = this.readArray<Record<string, unknown>>(this.communicationsKey);
    this.write(this.communicationsKey, [item, ...items]);
  }

  setLeadStage(id: string, stage: string): void {
    const current = this.selectedLead();
    if (current?.id === id) this.selectLead({ ...current, stage });
    const items = this.readArray<Record<string, unknown>>(this.createdLeadsKey);
    this.write(this.createdLeadsKey, items.map((x) => x['id'] === id ? { ...x, stage } : x));
  }

  private readArray<T>(key: string): T[] {
    return this.read<T[]>(key) ?? [];
  }

  private read<T>(key: string): T | null {
    try {
      if (typeof localStorage === 'undefined') return null;
      const raw = localStorage.getItem(key);
      return raw ? JSON.parse(raw) as T : null;
    } catch {
      return null;
    }
  }

  private write(key: string, value: unknown): void {
    try {
      if (typeof localStorage !== 'undefined') localStorage.setItem(key, JSON.stringify(value));
    } catch {
      // Storage can be unavailable in privacy/SSR contexts; in-memory signals still preserve the current flow.
    }
  }
}
