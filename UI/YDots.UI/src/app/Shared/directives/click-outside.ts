import { Directive, ElementRef, HostListener, inject, input, output } from '@angular/core';

/**
 * Auto-dismiss helper for overlay UI (dropdowns, combo lists, row menus,
 * filter panels and pop-ups).
 *
 * Emits `appClickOutside` when the user presses the pointer down anywhere
 * outside the host element — and outside any element passed through
 * `clickOutsideExcept` (typically the trigger button that toggles the panel,
 * so re-clicking the trigger doesn't fire a spurious close-then-reopen).
 *
 * Usage:
 *   <div class="menu" appClickOutside (appClickOutside)="open.set(false)"> … </div>
 *
 *   <button #trigger (click)="toggle()">…</button>
 *   @if (open()) {
 *     <div class="panel" appClickOutside [clickOutsideExcept]="trigger"
 *          (appClickOutside)="open.set(false)"> … </div>
 *   }
 */
@Directive({
  selector: '[appClickOutside]',
})
export class ClickOutsideDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Element(s) to also treat as "inside" — usually the toggle that opens the panel. */
  readonly clickOutsideExcept = input<HTMLElement | readonly HTMLElement[] | null>(null);

  /** Fires once per outside press while the host is in the DOM. */
  readonly appClickOutside = output<void>();

  // `pointerdown` (not `click`) so the panel dismisses the moment the user
  // presses elsewhere, matching native menu/dropdown behaviour.
  @HostListener('document:pointerdown', ['$event'])
  protected onDocumentPointerDown(event: Event): void {
    const target = event.target as Node | null;
    if (!target) return;

    // A press inside the panel itself is never "outside".
    if (this.host.nativeElement.contains(target)) return;

    // Nor is a press on an explicitly excluded element (e.g. the trigger).
    const except = this.clickOutsideExcept();
    if (except) {
      const list = Array.isArray(except) ? except : [except as HTMLElement];
      if (list.some((el) => el && el.contains(target))) return;
    }

    this.appClickOutside.emit();
  }
}
