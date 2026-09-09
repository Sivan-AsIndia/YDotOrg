import { CommonModule } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  REMIX_ICON_GROUPS,
  isRenderableIcon,
  remixIconClass,
} from '../../models/remix-icon-catalogue';

/**
 * Choosing an icon, from every icon the application can actually draw.
 *
 * WHAT IT REPLACES. A text box. The menu screens asked an administrator to type an icon name,
 * and the only names that worked were the fifty hard-coded in `NavigationService.ICONS` - which
 * were not written down anywhere a person could see. A typo produced no error and no icon, and
 * the fifty-first icon required a developer and a release.
 *
 * THE WHOLE FONT IS OFFERED because the whole font is already loaded: `icons.min.css` ships it
 * for the entire application, so an icon costs nothing extra to use and there is no reason to
 * ration them. See `remix-icon-catalogue.ts` for why only the names are bundled.
 *
 * IT RENDERS A CAPPED PAGE RATHER THAN THREE THOUSAND NODES. Putting the full set in the DOM
 * costs about 3000 elements and makes the panel visibly stutter on open; a virtual scroller
 * would fix that at the cost of a dependency and a fixed-height container that fights the
 * responsive layout. Capping the visible page and telling the reader how many more matched is
 * simpler, and search is how somebody finds a specific icon anyway - nobody scrolls to
 * "wheelchair" past two thousand others.
 */
@Component({
  selector: 'app-icon-picker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './icon-picker.html',
  styleUrl: './icon-picker.css',
})
export class IconPickerComponent {
  /** The icon currently chosen, in either form: `dashboard-line` or `ri-dashboard-line`. */
  readonly value = input<string | null | undefined>(null);

  /** Shown when nothing is chosen, so the empty state says something useful. */
  readonly placeholder = input<string>('No icon');

  /**
   * What the node would show if this override were cleared.
   *
   * Only the menu screens pass it, and only for a platform node: "clear" there does not mean no
   * icon, it means back to the one the product ships. Saying so is the difference between a
   * button somebody presses confidently and one they avoid.
   */
  readonly inheritedIcon = input<string | null | undefined>(null);

  readonly changed = output<string | null>();

  protected readonly open = signal(false);
  protected readonly search = signal('');
  protected readonly category = signal<string>('');

  /** How many icons are drawn before the rest are summarised. See the class note. */
  private static readonly PageSize = 144;

  protected readonly groups = REMIX_ICON_GROUPS;
  protected readonly categories = REMIX_ICON_GROUPS.map((group) => group.category);

  protected readonly selectedClass = computed(() => remixIconClass(this.value()));
  protected readonly inheritedClass = computed(() => remixIconClass(this.inheritedIcon()));

  /** The chosen name in its bare form, for display and for the equality test in the grid. */
  protected readonly selectedName = computed(() =>
    (this.value() ?? '').trim().replace(/^ri-/, ''),
  );

  /**
   * Every name matching the search and category, before the page cap.
   *
   * MATCHES ANYWHERE IN THE NAME, not only at the start. Remix names read back-to-front for
   * search purposes - the useful word in `arrow-left-circle-line` is as likely to be "circle" as
   * "arrow" - so an anchored match would hide most of what somebody means.
   */
  protected readonly matches = computed<readonly string[]>(() => {
    const term = this.search().trim().toLowerCase().replace(/^ri-/, '');
    const category = this.category();

    const pool = category
      ? (REMIX_ICON_GROUPS.find((group) => group.category === category)?.names ?? [])
      : REMIX_ICON_GROUPS.flatMap((group) => group.names);

    return term ? pool.filter((name) => name.includes(term)) : pool;
  });

  protected readonly visible = computed(() => this.matches().slice(0, IconPickerComponent.PageSize));
  protected readonly hiddenCount = computed(() =>
    Math.max(0, this.matches().length - IconPickerComponent.PageSize),
  );

  protected iconClassFor(name: string): string {
    return remixIconClass(name);
  }

  protected toggle(): void {
    this.open.update((v) => !v);
  }

  protected close(): void {
    this.open.set(false);
  }

  protected choose(name: string): void {
    this.changed.emit(name);
    this.open.set(false);
  }

  /** Clears the choice. On a platform node that means "inherit"; the template says which. */
  protected clear(): void {
    this.changed.emit(null);
    this.open.set(false);
  }

  /**
   * Accepts a name typed rather than clicked.
   *
   * A PERSON WHO KNOWS THE NAME SHOULD NOT HAVE TO HUNT FOR IT, and somebody pasting
   * `ri-heart-line` from a design note is doing something reasonable. It is only accepted when
   * the font can actually draw it - an unrenderable name saved silently is the failure the
   * picker exists to end.
   */
  protected applyTyped(): void {
    const typed = this.search().trim();

    if (isRenderableIcon(typed)) {
      this.choose(typed.replace(/^ri-/, ''));
    }
  }

  protected get typedIsRenderable(): boolean {
    return isRenderableIcon(this.search().trim());
  }
}
