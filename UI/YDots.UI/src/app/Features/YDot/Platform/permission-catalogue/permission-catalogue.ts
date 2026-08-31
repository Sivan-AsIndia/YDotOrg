import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { IamAdminApiService } from '../../../../Service/iam-admin-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PermissionMatrixResponse } from '../../../../Shared/models/iam-contract.model';

/**
 * The permission catalogue: every code the product defines, and what each one lets somebody do.
 *
 * READ-ONLY, AND THAT IS NOT AN OVERSIGHT. A permission code is not configuration — it is the
 * name of a check written into the API. Adding a row here would produce a permission nothing
 * enforces, which is worse than not having it: it would appear on the role editor, be granted in
 * good faith, and grant nothing. Codes arrive with the code that checks them.
 *
 * WHAT THE SCREEN IS ACTUALLY FOR
 * -------------------------------
 * Answering "what does this permission let somebody do" and "which permission do I need to grant
 * for X" — the two questions that come up whenever somebody is deciding what a role should hold.
 * Grouped by module and by group, because a flat list of a hundred and thirty codes cannot be
 * reasoned about.
 */
@Component({
  selector: 'app-permission-catalogue',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './permission-catalogue.html',
  styleUrl: './permission-catalogue.css',
})
export class PermissionCatalogueComponent implements OnInit, OnDestroy {
  private readonly api = inject(IamAdminApiService);
  private readonly destroy$ = new Subject<void>();

  readonly matrix = signal<PermissionMatrixResponse | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly errorMessage = signal('');

  readonly search = signal('');
  readonly expandedModules = signal<Set<string>>(new Set());

  readonly modules = computed(() => this.matrix()?.modules ?? []);

  readonly totalPermissions = computed(() =>
    this.modules().reduce(
      (total, module) => total + (module.groups ?? []).reduce(
        (groupTotal, group) => groupTotal + (group.permissions?.length ?? 0), 0), 0));

  /**
   * The modules, filtered by the search.
   *
   * The filter matches the code as well as the name, because half the time somebody arrives here
   * holding a code from an error message rather than a description.
   */
  readonly filteredModules = computed(() => {
    const term = this.search().trim().toLowerCase();

    if (!term) {
      return this.modules();
    }

    return this.modules()
      .map((module) => ({
        ...module,
        groups: (module.groups ?? [])
          .map((group) => ({
            ...group,
            permissions: (group.permissions ?? []).filter((permission) =>
              (permission.code ?? '').toLowerCase().includes(term)
              || (permission.name ?? '').toLowerCase().includes(term)
              || (permission.description ?? '').toLowerCase().includes(term)),
          }))
          .filter((group) => (group.permissions?.length ?? 0) > 0),
      }))
      .filter((module) => (module.groups?.length ?? 0) > 0);
  });

  readonly matchCount = computed(() =>
    this.filteredModules().reduce(
      (total, module) => total + (module.groups ?? []).reduce(
        (groupTotal, group) => groupTotal + (group.permissions?.length ?? 0), 0), 0));

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);

    this.api
      .getPermissionMatrix()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (matrix) => {
          this.matrix.set(matrix);
          this.loading.set(false);

          // The first module opens by itself, so the screen is not a wall of collapsed headings
          // with nothing to read.
          const [first] = matrix.modules ?? [];
          if (first?.moduleCode) {
            this.expandedModules.set(new Set([first.moduleCode]));
          }
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(apiErrorMessage(error, 'The catalogue could not be loaded.'));
        },
      });
  }

  isExpanded(moduleCode: string | null | undefined): boolean {
    // Searching opens everything: hiding matches behind a collapsed heading defeats the search.
    return this.search().trim().length > 0 || this.expandedModules().has(moduleCode ?? '');
  }

  toggleModule(moduleCode: string | null | undefined): void {
    if (!moduleCode) {
      return;
    }

    this.expandedModules.update((current) => {
      const next = new Set(current);

      if (next.has(moduleCode)) {
        next.delete(moduleCode);
      } else {
        next.add(moduleCode);
      }

      return next;
    });
  }

  expandAll(): void {
    this.expandedModules.set(
      new Set(this.modules().map((module) => module.moduleCode ?? '').filter(Boolean)));
  }

  collapseAll(): void {
    this.expandedModules.set(new Set());
  }

  /**
   * The colour for an action.
   *
   * Reading is safe, writing changes things, and approving or deleting is where somebody should
   * look twice. Colouring by that rather than one shade per verb is what makes the list scannable
   * for "what can this role actually do to my data".
   */
  actionClass(action: string | null | undefined): string {
    switch (action) {
      case 'view':
      case 'export':
        return 'bg-info-subtle text-info';

      case 'create':
      case 'edit':
        return 'bg-primary-subtle text-primary';

      case 'approve':
      case 'reject':
        return 'bg-warning-subtle text-warning';

      case 'delete':
      case 'administer':
        return 'bg-danger-subtle text-danger';

      default:
        return 'bg-secondary-subtle text-secondary';
    }
  }

  /** Copies a code, which is what most visits to this screen end in. */
  copyCode(code: string | null | undefined): void {
    if (code) {
      void navigator.clipboard.writeText(code);
    }
  }
}
