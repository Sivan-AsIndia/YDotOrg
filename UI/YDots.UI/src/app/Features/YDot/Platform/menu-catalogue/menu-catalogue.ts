import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { IamAdminApiService } from '../../../../Service/iam-admin-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { MenuNode } from '../../../../Shared/models/iam-contract.model';
import { NavigationService } from '../../../../Shared/services/navigation.service';

/**
 * The platform navigation catalogue: every screen the product has, across every Organisation.
 *
 * WHAT THIS IS, AND WHAT IT IS NOT
 * ---------------------------------
 * This is the PRODUCT'S menu — the full set of screens that exist, the routes they live at, and
 * the permission each one requires. It is the top of the four-way intersection that decides what
 * anybody sees:
 *
 *     this catalogue        ← here
 *   ∩ organisation config   ← Administration → Menu and navigation
 *   ∩ role mapping          ← the same screen, second tab
 *   ∩ held permissions
 *
 * Turning something on for one Organisation is not done here — a change here would affect every
 * customer at once. This screen answers "what does the product contain, and what does each part
 * require", which is the question somebody has when deciding what to enable, or when a screen
 * appears to be missing for a whole Organisation.
 *
 * WHY IT IS READ-ONLY IN PRACTICE
 * -------------------------------
 * A catalogue entry names a route that has to exist in the client and a permission that has to
 * be enforced by the API. Adding one from a form produces a menu item that leads to a blank page,
 * or one guarded by a permission nothing checks. Entries arrive with the code they describe.
 */
@Component({
  selector: 'app-menu-catalogue',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './menu-catalogue.html',
  styleUrl: './menu-catalogue.css',
})
export class MenuCatalogueComponent implements OnInit, OnDestroy {
  private readonly api = inject(IamAdminApiService);
  private readonly navigation = inject(NavigationService);
  private readonly destroy$ = new Subject<void>();

  readonly nodes = signal<MenuNode[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly errorMessage = signal('');
  readonly search = signal('');

  /** Flattened for the table, so one row shape serves all three levels. */
  readonly rows = computed(() => {
    const flat: (MenuNode & { depth: number })[] = [];

    const walk = (list: MenuNode[], depth: number): void => {
      for (const node of list) {
        flat.push({ ...node, depth });
        walk(node.children ?? [], depth + 1);
      }
    };

    walk(this.nodes(), 0);
    return flat;
  });

  readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();

    if (!term) {
      return this.rows();
    }

    // Matching the route and the permission as well as the name, because somebody arriving here
    // usually holds one of those rather than the label.
    return this.rows().filter((node) =>
      (node.name ?? '').toLowerCase().includes(term)
      || (node.code ?? '').toLowerCase().includes(term)
      || (node.route ?? '').toLowerCase().includes(term)
      || (node.requiredPermissionCode ?? '').toLowerCase().includes(term)
      || (node.moduleCode ?? '').toLowerCase().includes(term));
  });

  readonly totalNodes = computed(() => this.rows().length);

  readonly moduleCount = computed(
    () => new Set(this.rows().map((node) => node.moduleCode).filter(Boolean)).size);

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

    // Platform nodes included: this screen is for a global caller, and the point of it is to see
    // everything the product has — including the parts only the platform team uses.
    this.api
      .getMenuCatalogue(true)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (nodes) => {
          this.nodes.set(nodes);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadFailed.set(true);
          this.errorMessage.set(apiErrorMessage(error, 'The catalogue could not be loaded.'));
        },
      });
  }

  iconClass(icon: string | null | undefined): string {
    return this.navigation.iconClass(icon);
  }

  indent(depth: number): string {
    return `${depth * 1.5}rem`;
  }

  levelLabel(level: string | undefined): string {
    switch (level) {
      case 'menu': return 'Menu';
      case 'subMenu': return 'Submenu';
      case 'childSubMenu': return 'Child submenu';
      default: return '';
    }
  }

  levelClass(level: string | undefined): string {
    switch (level) {
      case 'menu': return 'bg-primary-subtle text-primary';
      case 'subMenu': return 'bg-info-subtle text-info';
      default: return 'bg-secondary-subtle text-secondary';
    }
  }
}
