import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable, Subject, takeUntil } from 'rxjs';
import { IamAdminApiService } from '../../../../Service/iam-admin-api.service';
import {
  OutcomeResponse,
  apiErrorMessage,
  apiFieldErrors,
} from '../../../../Shared/models/api-response.model';
import {
  DepartmentResponse,
  OrganisationUnitResponse,
  RecordStatus,
} from '../../../../Shared/models/iam-contract.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { ToastService } from '../../../../Shared/services/toast.service';

type Mode = 'departments' | 'units';

/**
 * Departments and organisation units.
 *
 * TWO SEPARATE HIERARCHIES, AND THAT IS DELIBERATE. A department is what somebody DOES —
 * Fundraising, Finance. A unit is where they SIT — Head office, Southern region. Most
 * organisations need both, and collapsing them into one tree forces a choice that has to be
 * undone later: a fundraiser in the southern office belongs to Fundraising AND to Southern, and
 * neither is a child of the other.
 *
 * The same screen serves both because the operations are identical — list, add, edit, retire —
 * and two near-identical components would drift within a month. Which one is being managed comes
 * from the route.
 *
 * WHY NOTHING IS DELETED WHILE IT IS IN USE
 * -----------------------------------------
 * A department with people in it, or with children beneath it, is refused by the server rather
 * than orphaning either. The member and child counts are shown on every row precisely so that
 * refusal is never a surprise — and setting one to inactive is offered as what was almost
 * certainly meant: retire it, keep the history.
 */
@Component({
  selector: 'app-organisation-structure',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './organisation-structure.html',
  styleUrl: './organisation-structure.css',
})
export class OrganisationStructureComponent implements OnInit, OnDestroy {
  private readonly api = inject(IamAdminApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly tokens = inject(AuthTokenService);
  private readonly toast = inject(ToastService);

  private readonly destroy$ = new Subject<void>();

  readonly mode = signal<Mode>('departments');

  readonly departments = signal<DepartmentResponse[]>([]);
  readonly units = signal<OrganisationUnitResponse[]>([]);

  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly errorMessage = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});

  /** Which row is open in the editor: an id, 'new', or null for closed. */
  readonly editing = signal<string | null>(null);

  readonly form = signal({
    name: '',
    code: '',
    description: '',
    parentId: '',
    headUserId: '',
    unitType: '',
    city: '',
    country: '',
    contactEmail: '',
    contactPhone: '',
    status: 'active' as RecordStatus,
    displayOrder: 0,
    expectedVersion: 0,
  });

  readonly confirmingDelete = signal<string | null>(null);

  // =========================================================================================
  // Derived
  // =========================================================================================

  readonly isDepartments = computed(() => this.mode() === 'departments');

  readonly title = computed(() => (this.isDepartments() ? 'Departments' : 'Offices and regions'));

  readonly subtitle = computed(() =>
    this.isDepartments()
      ? 'What people do: the functions this organisation is organised into.'
      : 'Where people sit: the offices, branches and regions this organisation operates from.');

  readonly canManage = computed(() =>
    this.tokens.hasPermission(
      this.isDepartments()
        ? 'iam.organisation.manage-departments'
        : 'iam.organisation.manage-units'));

  /** The rows, sorted so parents come before their children and the tree reads top to bottom. */
  readonly rows = computed(() =>
    this.isDepartments()
      ? this.sortTree(this.departments(), (item) => item.parentDepartmentId)
      : this.sortTree(this.units(), (item) => item.parentUnitId));

  /** Parent options for the editor, minus the row being edited and anything beneath it. */
  readonly parentOptions = computed(() => {
    const current = this.editing();

    if (this.isDepartments()) {
      return this.departments().filter((item) => item.id !== current);
    }

    return this.units().filter((item) => item.id !== current);
  });

  // =========================================================================================
  // Lifecycle
  // =========================================================================================

  ngOnInit(): void {
    // The route decides which hierarchy this is. Reading it from the data rather than the URL
    // text means the two routes can be renamed without touching the component.
    const mode = this.route.snapshot.data['mode'] as Mode | undefined;
    this.mode.set(mode ?? 'departments');

    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.editing.set(null);

    const request: Observable<DepartmentResponse[] | OrganisationUnitResponse[]> =
      this.isDepartments() ? this.api.getDepartments() : this.api.getUnits();

    request.pipe(takeUntil(this.destroy$)).subscribe({
      next: (items) => {
        if (this.isDepartments()) {
          this.departments.set(items as DepartmentResponse[]);
        } else {
          this.units.set(items as OrganisationUnitResponse[]);
        }

        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadFailed.set(true);
        this.errorMessage.set(apiErrorMessage(error, 'That could not be loaded.'));
      },
    });
  }

  // =========================================================================================
  // The editor
  // =========================================================================================

  startAdding(): void {
    this.editing.set('new');
    this.errorMessage.set('');
    this.fieldErrors.set({});

    this.form.set({
      name: '', code: '', description: '', parentId: '', headUserId: '',
      unitType: '', city: '', country: '', contactEmail: '', contactPhone: '',
      status: 'active', displayOrder: this.rows().length * 10,
      expectedVersion: 0,
    });
  }

  startEditing(row: DepartmentResponse | OrganisationUnitResponse): void {
    if (!row.id) {
      return;
    }

    this.editing.set(row.id);
    this.errorMessage.set('');
    this.fieldErrors.set({});

    const asUnit = row as OrganisationUnitResponse;
    const asDepartment = row as DepartmentResponse;

    this.form.set({
      name: row.name ?? '',
      code: row.code ?? '',
      description: row.description ?? '',
      parentId: (this.isDepartments()
        ? asDepartment.parentDepartmentId
        : asUnit.parentUnitId) ?? '',
      headUserId: asDepartment.headUserId ?? '',
      unitType: asUnit.unitType ?? '',
      city: asUnit.city ?? '',
      country: asUnit.country ?? '',
      contactEmail: asUnit.contactEmail ?? '',
      contactPhone: asUnit.contactPhone ?? '',
      status: row.status ?? 'active',
      displayOrder: row.displayOrder ?? 0,
      expectedVersion: row.version ?? 0,
    });
  }

  cancelEditing(): void {
    this.editing.set(null);
    this.errorMessage.set('');
    this.fieldErrors.set({});
  }

  update<K extends keyof ReturnType<typeof this.form>>(
    key: K, value: ReturnType<typeof this.form>[K]): void {
    this.form.update((current) => ({ ...current, [key]: value }));
  }

  readonly canSave = computed(() => {
    const f = this.form();
    return f.name.trim().length >= 2 && f.code.trim().length >= 2 && !this.saving();
  });

  save(): void {
    if (!this.canSave()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');
    this.fieldErrors.set({});

    const f = this.form();
    const id = this.editing();
    const isNew = id === 'new';

    const request: Observable<DepartmentResponse | OrganisationUnitResponse> =
      this.isDepartments() ? this.saveDepartment(isNew, id, f) : this.saveUnit(isNew, id, f);

    request.pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(null);
        this.toast.show(
          'Saved',
          isNew ? `${f.name} has been added.` : `${f.name} has been updated.`,
          'success');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.errorMessage.set(apiErrorMessage(error, 'That could not be saved.'));
        this.fieldErrors.set(apiFieldErrors(error));
      },
    });
  }

  private saveDepartment(isNew: boolean, id: string | null, f: ReturnType<typeof this.form>) {
    if (isNew) {
      return this.api.createDepartment({
        name: f.name.trim(),
        code: f.code.trim().toUpperCase(),
        description: f.description.trim() || null,
        parentDepartmentId: f.parentId || null,
        headUserId: f.headUserId || null,
        displayOrder: f.displayOrder,
      });
    }

    return this.api.updateDepartment(id!, {
      expectedVersion: f.expectedVersion,
      name: f.name.trim(),
      code: f.code.trim().toUpperCase(),
      description: f.description.trim() || null,
      parentDepartmentId: f.parentId || null,
      headUserId: f.headUserId || null,
      status: f.status,
      displayOrder: f.displayOrder,
    });
  }

  private saveUnit(isNew: boolean, id: string | null, f: ReturnType<typeof this.form>) {
    if (isNew) {
      return this.api.createUnit({
        name: f.name.trim(),
        code: f.code.trim().toUpperCase(),
        description: f.description.trim() || null,
        parentUnitId: f.parentId || null,
        unitType: f.unitType.trim() || null,
        city: f.city.trim() || null,
        country: f.country.trim() || null,
        contactEmail: f.contactEmail.trim() || null,
        contactPhone: f.contactPhone.trim() || null,
        displayOrder: f.displayOrder,
      });
    }

    return this.api.updateUnit(id!, {
      expectedVersion: f.expectedVersion,
      name: f.name.trim(),
      code: f.code.trim().toUpperCase(),
      description: f.description.trim() || null,
      parentUnitId: f.parentId || null,
      unitType: f.unitType.trim() || null,
      city: f.city.trim() || null,
      country: f.country.trim() || null,
      contactEmail: f.contactEmail.trim() || null,
      contactPhone: f.contactPhone.trim() || null,
      status: f.status,
      displayOrder: f.displayOrder,
    });
  }

  // =========================================================================================
  // Removing
  // =========================================================================================

  confirmDelete(row: DepartmentResponse | OrganisationUnitResponse): void {
    this.confirmingDelete.set(row.id ?? null);
    this.errorMessage.set('');
  }

  cancelDelete(): void {
    this.confirmingDelete.set(null);
  }

  /**
   * Removes a department or unit.
   *
   * Refused by the server while anybody is in it or anything sits under it. The counts on each
   * row are shown so that refusal is never a surprise, and the message that comes back names the
   * exact obstacle rather than saying "cannot delete".
   */
  delete(row: DepartmentResponse | OrganisationUnitResponse): void {
    if (!row.id || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.errorMessage.set('');

    const request = { expectedVersion: row.version ?? 0 };

    const call: Observable<OutcomeResponse> = this.isDepartments()
      ? this.api.deleteDepartment(row.id, request)
      : this.api.deleteUnit(row.id, request);

    call.pipe(takeUntil(this.destroy$)).subscribe({
      next: (outcome) => {
        this.saving.set(false);
        this.confirmingDelete.set(null);
        this.toast.show('Removed', outcome.message ?? `${row.name} has been removed.`, 'success');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.confirmingDelete.set(null);
        this.errorMessage.set(apiErrorMessage(error, 'That could not be removed.'));
      },
    });
  }

  // =========================================================================================
  // Display helpers
  // =========================================================================================

  /**
   * Orders a flat list so each parent is immediately followed by its children.
   *
   * The server returns them flat with a parent id, which is the right shape to store. Turning it
   * into reading order here rather than nesting it in the template keeps the row markup to one
   * shape at any depth.
   */
  private sortTree<T extends { id?: string; name?: string | null; displayOrder?: number }>(
    items: T[],
    parentOf: (item: T) => string | null | undefined,
  ): (T & { depth: number })[] {
    const ordered: (T & { depth: number })[] = [];

    const append = (parentId: string | null, depth: number): void => {
      // A guard against a cycle: the server refuses to create one, but a screen that hangs is a
      // far worse failure than one that renders a node in the wrong place.
      if (depth > 8) {
        return;
      }

      items
        .filter((item) => (parentOf(item) ?? null) === parentId)
        .sort((left, right) =>
          (left.displayOrder ?? 0) - (right.displayOrder ?? 0)
          || (left.name ?? '').localeCompare(right.name ?? ''))
        .forEach((item) => {
          ordered.push({ ...item, depth });
          append(item.id ?? null, depth + 1);
        });
    };

    append(null, 0);

    // Anything whose parent is missing from the list would otherwise vanish. Appending it flat is
    // better than dropping a real record because its parent was retired.
    const seen = new Set(ordered.map((item) => item.id));
    items.filter((item) => !seen.has(item.id)).forEach((item) => ordered.push({ ...item, depth: 0 }));

    return ordered;
  }

  indent(depth: number): string {
    return `${depth * 1.5}rem`;
  }

  statusClass(status: string | undefined): string {
    return status === 'active'
      ? 'bg-success-subtle text-success'
      : 'bg-secondary-subtle text-secondary';
  }

  memberCount(row: DepartmentResponse | OrganisationUnitResponse): number {
    return row.memberCount ?? 0;
  }

  childCount(row: DepartmentResponse | OrganisationUnitResponse): number {
    return row.childCount ?? 0;
  }

  /** Whether removal will be refused, so the button can say so before it is pressed. */
  canRemove(row: DepartmentResponse | OrganisationUnitResponse): boolean {
    return this.memberCount(row) === 0 && this.childCount(row) === 0;
  }
}
