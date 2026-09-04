import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { LayoutService } from '../../Service/layout-service';
import { MenuNode } from '../models/auth.model';
import { AuthTokenService } from '../services/auth-token.service';
import { CurrentUserService } from '../services/current-user.service';
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
 *
 * IT NO LONGER FETCHES ANYTHING ON MOUNT. It used to load the tree when it found the menu empty,
 * which is a rule that reads sensibly and is wrong in the one case that matters: after an
 * Organisation switch the menu is not empty, it is FULL — of the previous Organisation's items —
 * so the condition was false and the stale tree stayed. Loading is `NavigationService`'s job now,
 * keyed to the Organisation rather than to emptiness. This component renders what it is given.
 */
@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.css',
})
export class SidebarComponent implements OnDestroy {
  readonly layoutService = inject(LayoutService);
  readonly navigation = inject(NavigationService);
  private readonly tokens = inject(AuthTokenService);
  private readonly currentUser = inject(CurrentUserService);

  private readonly destroy$ = new Subject<void>();

  /**
   * The server tree, with three presentation-only overrides applied on top.
   *
   * THESE ARE LABEL/LAYOUT CHANGES, NOT PERMISSION CHANGES. The menu is still exactly what the
   * server decided this person may see (see the class doc); this only renames one node's label,
   * drops another, and mirrors Campaign Overview's entry under a new label so Tracking Asset
   * Manager has a way in from the sidebar. None of the three touches `requiredPermissionCode`,
   * `route`, or anything the server sent for access control — a node that would not have
   * appeared still will not, and a node that appears is still gated exactly as the server gated
   * it. See `overrideMenu` for the mechanics.
   */
  readonly menu = computed(() => this.overrideMenu(this.navigation.menu()));
  readonly loading = computed(() => this.navigation.loading());
  readonly failed = computed(() => this.navigation.failed());

  /** Absolute route of the campaign wizard's "Create Campaign" link, dropped from the sidebar. */
  private static readonly CREATE_CAMPAIGN_ROUTE_RE = /\/campaign-wizard$/;
  /** Absolute route of the campaign list — relabelled "Campaign Overview" here. */
  private static readonly CAMPAIGN_REGISTER_ROUTE_RE = /\/campaign-register$/;
  /** Where the injected Tracking Asset Manager link points. */
  private static readonly TRACKING_ASSET_ROUTE = '/app/fundraising/campaigns/tracking-asset-manager';

  private isCreateCampaignNode(node: MenuNode): boolean {
    return !!node.route && SidebarComponent.CREATE_CAMPAIGN_ROUTE_RE.test(node.route);
  }

  private isCampaignRegisterNode(node: MenuNode): boolean {
    return !!node.route && SidebarComponent.CAMPAIGN_REGISTER_ROUTE_RE.test(node.route);
  }

  /**
   * Drops "Create Campaign", relabels "Campaign Register" to "Campaign Overview", and adds a
   * "Tracking Asset Manager" link right beside it — recursively, so it applies at whichever
   * depth the server nested the campaigns group.
   */
  private overrideMenu(nodes: readonly MenuNode[]): MenuNode[] {
    return nodes.filter((n) => !this.isCreateCampaignNode(n)).map((n) => this.overrideNode(n));
  }

  private overrideNode(node: MenuNode): MenuNode {
    if (!node.children || node.children.length === 0) {
      return this.isCampaignRegisterNode(node) ? { ...node, name: 'Campaign Overview' } : node;
    }

    let children = this.overrideMenu(node.children);

    const registerIndex = children.findIndex((c) => this.isCampaignRegisterNode(c));
    const alreadyHasTrackingLink = children.some(
      (c) => c.route === SidebarComponent.TRACKING_ASSET_ROUTE,
    );
    // Gated the same way the campaign-detail page gates its own link to this screen: a person
    // without `cam.tracking-assets.view` would only reach a route guard that turns them away.
    const canSeeTrackingAssets = this.currentUser.hasPermission('cam.tracking-assets.view');
    if (registerIndex !== -1 && !alreadyHasTrackingLink && canSeeTrackingAssets) {
      const register = children[registerIndex];
      const trackingNode: MenuNode = {
        ...register,
        id: 'client-tracking-asset-manager',
        code: 'client-tracking-asset-manager',
        name: 'Tracking Asset Manager',
        route: SidebarComponent.TRACKING_ASSET_ROUTE,
        icon: 'clipboard',
        children: null,
        isGroupOnly: false,
        hasChildren: false,
      };
      children = [
        ...children.slice(0, registerIndex + 1),
        trackingNode,
        ...children.slice(registerIndex + 1),
      ];
    }

    return { ...node, children };
  }

  /** Shown in the sidebar footer so a root user always knows whose data they are looking at. */
  readonly organisationName = computed(() => this.tokens.organisationName());
  readonly isActingInOrganisation = computed(() => this.tokens.isActingInOrganisation());

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
