import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { LayoutService } from '../../Service/layout-service';
import { MenuNode } from '../models/auth.model';
import { AuthTokenService } from '../services/auth-token.service';
import { NavigationService } from '../services/navigation.service';

/**
 * The sidebar.
 *
 * EVERY ITEM COMES FROM THE SERVER. This used to be a twelve-hundred-line hand-written tree, and
 * the trouble with that is not its length — it is that a hand-written menu shows every link to
 * everybody. What a person may see depends on what the product has, what their Organisation has
 * enabled, what their role was given and what permissions they hold, and only the server knows
 * all four. See `NavigationService` for the rest of that reasoning.
 *
 * THREE LEVELS, RENDERED AS THREE LEVELS. Menu, submenu and child submenu each have their own
 * markup in the theme, so the template handles them explicitly rather than recursing — which
 * also means a fourth level, if one ever appeared, would be visibly missing rather than silently
 * flattened.
 */
@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.css',
})
export class SidebarComponent implements OnInit, OnDestroy {
  readonly layoutService = inject(LayoutService);
  readonly navigation = inject(NavigationService);
  private readonly tokens = inject(AuthTokenService);

  private readonly destroy$ = new Subject<void>();

  readonly menu = computed(() => this.navigation.menu());
  readonly loading = computed(() => this.navigation.loading());
  readonly failed = computed(() => this.navigation.failed());

  /** Shown in the sidebar footer so a root user always knows whose data they are looking at. */
  readonly organisationName = computed(() => this.tokens.organisationName());
  readonly isActingInOrganisation = computed(() => this.tokens.isActingInOrganisation());

  ngOnInit(): void {
    // Only when there is nothing yet: the shell loads it on sign-in and again after every
    // Organisation switch, and re-fetching on each render would be a request per navigation.
    if (this.navigation.menu().length === 0) {
      this.navigation.load().pipe(takeUntil(this.destroy$)).subscribe({
        // Failure is handled through the service's `failed` signal, which the template renders.
        error: () => undefined,
      });
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  retry(): void {
    this.navigation.load().pipe(takeUntil(this.destroy$)).subscribe({ error: () => undefined });
  }

  iconClass(node: MenuNode): string {
    return this.navigation.iconClass(node.icon);
  }

  collapseId(node: MenuNode): string {
    return this.navigation.collapseId(node);
  }

  /**
   * Whether a node opens a page or only opens a group.
   *
   * A group with no route is a heading; giving it a link would navigate somewhere that does not
   * exist. The server marks these explicitly rather than leaving it to be inferred from a
   * missing route, so the two cannot disagree.
   */
  isGroup(node: MenuNode): boolean {
    return node.isGroupOnly === true || (node.hasChildren === true && !node.route);
  }

  children(node: MenuNode): MenuNode[] {
    return node.children ?? [];
  }

  trackByCode(_index: number, node: MenuNode): string {
    return node.code ?? node.id ?? String(_index);
  }

  // ---- Theme panel, unchanged ------------------------------------------------------------

  get themePanelOpen(): boolean {
    return this.layoutService.themePanelOpen;
  }

  get themeMenuOpen(): boolean {
    return this.layoutService.themeMenuOpen;
  }

  toggleThemePanel(): void {
    this.layoutService.toggleThemePanel();
  }

  /** Toggles only the Theme Settings dropdown, without opening the panel. */
  toggleThemeMenu(): void {
    this.layoutService.toggleThemeMenu();
  }

  /** Opens the theme panel with a specific section active. */
  openThemeSection(section: string): void {
    this.layoutService.openThemeSection(section);
  }
}
