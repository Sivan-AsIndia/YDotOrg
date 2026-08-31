import { CommonModule } from '@angular/common';
import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ClickOutsideDirective } from '../../../../../Shared/directives/click-outside';
import { ReadinessOwnerOption } from '../../../../../Shared/models/campaign-readiness.model';
import {
  ReadinessCheck,
  ReadinessCheckCategory,
  ReadinessCheckType,
  ReadinessEvidenceType,
  ReadinessSourceType,
  ReadinessValidationMethod,
} from '../../../../../Shared/models/campaign-readiness-checklist.model';

/**
 * Add Readiness Check — standalone right-side off-canvas.
 *
 * Additive to CAM-UI-07. Opens over the readiness checklist page (no route
 * change); the page stays visible and interactive behind the backdrop. Captures
 * a single readiness check's *configuration* only — never a computed result.
 * The check is authored as Pending; there is no manual status control anywhere.
 *
 * Close (X / backdrop / Cancel) is guarded by a discard-changes confirmation
 * whenever the form has unsaved input, matching the page's existing
 * confirm-dialog pattern.
 */
@Component({
  selector: 'app-add-readiness-check',
  imports: [CommonModule, FormsModule, ClickOutsideDirective],
  templateUrl: './add-readiness-check.html',
  styleUrl: './add-readiness-check.css',
})
export class AddReadinessCheckComponent {
  /** Scope-aware owner options (reused from the checklist page's directory). */
  readonly ownerOptions = input<readonly ReadinessOwnerOption[]>([]);
  /** When set, the drawer opens pre-filled for that check and Save replaces it in place
   *  instead of authoring a new one — drives the row "Edit" action. */
  readonly editing = input<ReadinessCheck | null>(null);
  protected readonly isEditMode = computed(() => this.editing() !== null);

  /** Emitted once the off-canvas has fully closed (after any discard guard). */
  readonly closed = output<void>();
  /** Emitted with the authored (or edited) check when Add/Save check succeeds. */
  readonly added = output<ReadinessCheck>();

  // ================= Reference data =================
  protected readonly categories: readonly ReadinessCheckCategory[] = [
    'Content', 'Budget', 'Tracking', 'Payment', 'Template', 'Consent',
  ];
  protected readonly checkTypes: readonly ReadinessCheckType[] = [
    'Manual', 'Automatic', 'Derived', 'External', 'Hybrid',
  ];
  protected readonly validationMethods: readonly ReadinessValidationMethod[] = [
    'Manual confirmation', 'Record exists', 'Approval completed', 'Count reached',
    'Integration verified', 'Document uploaded', 'Status matches', 'Date condition',
    'External system validation', 'Custom rule',
  ];
  protected readonly sourceTypes: readonly ReadinessSourceType[] = [
    'Internal', 'External', 'Derived', 'Manual', 'Stub', 'Integration',
  ];
  protected readonly evidenceTypes: readonly ReadinessEvidenceType[] = [
    'Document', 'URL', 'Screenshot', 'Record',
  ];
  /** Source types for which a Source reference is relevant (shown conditionally). */
  private readonly sourceRefRelevant: ReadonlySet<ReadinessSourceType> = new Set<ReadinessSourceType>([
    'External', 'Integration', 'Stub', 'Derived',
  ]);

  private ownerName(ref?: string): string {
    if (!ref) return '';
    return this.ownerOptions().find((o) => o.reference === ref)?.name ?? ref;
  }

  // ================= Form state =================
  protected readonly submitted = signal(false);
  protected readonly cName = signal('');
  protected readonly cDescription = signal('');
  protected readonly cCategory = signal<ReadinessCheckCategory | ''>('');
  protected readonly cCheckType = signal<ReadinessCheckType | ''>('');
  protected readonly cValidationMethod = signal<ReadinessValidationMethod | ''>('');
  protected readonly cSuccessCriteria = signal('');
  protected readonly cRequiredForLaunch = signal(true);
  protected readonly cOwnerId = signal('');
  protected readonly cDueDate = signal('');
  protected readonly cSourceType = signal<ReadinessSourceType | ''>('');
  protected readonly cSourceReference = signal('');
  protected readonly cEvidenceRequired = signal(false);
  protected readonly cEvidenceType = signal<ReadinessEvidenceType | ''>('');
  protected readonly cEvidenceReference = signal('');
  protected readonly cNotes = signal('');

  /** Snapshot of every field's opening value — an empty form when authoring, or the check's own
   *  values when editing — so isDirty() only flags fields actually changed from where they opened. */
  private openSnapshot = '';
  private currentSnapshot(): string {
    return JSON.stringify([
      this.cName(), this.cDescription(), this.cCategory(), this.cCheckType(), this.cValidationMethod(),
      this.cSuccessCriteria(), this.cRequiredForLaunch(), this.cOwnerId(), this.cDueDate(), this.cSourceType(),
      this.cSourceReference(), this.cEvidenceRequired(), this.cEvidenceType(), this.cEvidenceReference(), this.cNotes(),
    ]);
  }

  constructor() {
    // Signal inputs aren't guaranteed to be bound yet inside the constructor body, so the
    // pre-fill reads `editing()` from a reactive effect instead — it fires once Angular has
    // actually applied the [editing] binding. The writes are untracked so this doesn't loop.
    effect(() => {
      const check = this.editing();
      untracked(() => {
        if (check) {
          this.cName.set(check.name);
          this.cDescription.set(check.description ?? '');
          this.cCategory.set(check.category);
          this.cCheckType.set(check.checkType);
          this.cValidationMethod.set(check.validationMethod);
          this.cSuccessCriteria.set(check.successCriteria);
          this.cRequiredForLaunch.set(check.requiredForLaunch);
          this.cOwnerId.set(check.ownerId ?? '');
          this.cDueDate.set(check.dueDate ?? '');
          this.cSourceType.set(check.sourceType);
          this.cSourceReference.set(check.sourceReference ?? '');
          this.cEvidenceRequired.set(check.evidenceRequired);
          this.cEvidenceType.set(check.evidenceType ?? '');
          this.cEvidenceReference.set(check.evidenceReference ?? '');
          this.cNotes.set(check.notes ?? '');
        }
        this.openSnapshot = this.currentSnapshot();
      });
    });
  }

  /** Source reference is shown only for source types where it is meaningful. */
  protected readonly showSourceReference = computed(
    () => this.cSourceType() !== '' && this.sourceRefRelevant.has(this.cSourceType() as ReadinessSourceType),
  );

  // ================= Owner combobox (searchable employee/user select) =================
  protected readonly ownerComboOpen = signal(false);
  protected readonly ownerQuery = signal('');
  protected readonly ownerFiltered = computed(() => {
    const q = this.ownerQuery().trim().toLowerCase();
    const all = this.ownerOptions();
    if (!q) return all;
    return all.filter((o) => `${o.name} ${o.context} ${o.reference}`.toLowerCase().includes(q));
  });
  protected readonly checkOwnerLabel = computed(() => this.ownerName(this.cOwnerId()));
  protected openOwnerCombo(): void {
    this.ownerComboOpen.set(true);
    this.ownerQuery.set('');
  }
  protected selectCheckOwner(ref: string): void {
    this.cOwnerId.set(ref);
    this.ownerComboOpen.set(false);
  }
  protected clearCheckOwner(): void {
    this.cOwnerId.set('');
    this.ownerComboOpen.set(false);
  }

  // ================= Validation =================
  protected readonly cNameValid = computed(() => this.cName().trim().length > 0);
  protected readonly cCategoryValid = computed(() => this.cCategory() !== '');
  protected readonly cSuccessValid = computed(() => this.cSuccessCriteria().trim().length > 0);
  protected readonly checkValid = computed(
    () =>
      this.cNameValid() &&
      this.cCategoryValid() &&
      this.cSuccessValid(),
  );

  // ================= Add check =================
  protected addCheck(): void {
    this.submitted.set(true);
    if (!this.checkValid()) return;
    const existing = this.editing();
    const built: ReadinessCheck = {
      id: existing?.id ?? `CHK-${Date.now()}-${Math.round(Math.random() * 1e4)}`,
      name: this.cName().trim(),
      description: this.cDescription().trim() || undefined,
      category: this.cCategory() as ReadinessCheckCategory,
      // Check type / validation method / source type / evidence are no longer captured in the
      // form — persist stable defaults so the stored shape stays valid.
      checkType: existing?.checkType ?? 'Manual',
      validationMethod: existing?.validationMethod ?? 'Manual confirmation',
      successCriteria: this.cSuccessCriteria().trim(),
      requiredForLaunch: this.cRequiredForLaunch(),
      ownerId: this.cOwnerId() || undefined,
      dueDate: this.cDueDate() || undefined,
      sourceType: existing?.sourceType ?? 'Manual',
      sourceReference: existing?.sourceReference,
      evidenceRequired: existing?.evidenceRequired ?? false,
      evidenceType: existing?.evidenceType,
      evidenceReference: existing?.evidenceReference,
      notes: this.cNotes().trim() || undefined,
      status: existing?.status ?? 'Pending',
    };
    this.added.emit(built);
    this.closed.emit();
  }

  // ================= Unsaved-changes guard =================
  protected readonly discardConfirmOpen = signal(false);

  /** Any field touched away from its opening value (empty when authoring, the check's own
   *  values when editing) counts as unsaved input. */
  protected isDirty(): boolean {
    return this.currentSnapshot() !== this.openSnapshot;
  }

  /** X / backdrop / Cancel entry point — guards on unsaved changes. */
  protected attemptClose(): void {
    if (this.isDirty()) {
      this.discardConfirmOpen.set(true);
      return;
    }
    this.closed.emit();
  }
  protected confirmDiscard(): void {
    this.discardConfirmOpen.set(false);
    this.closed.emit();
  }
  protected cancelDiscard(): void {
    this.discardConfirmOpen.set(false);
  }
}
