import { CommonModule } from '@angular/common';
import { Component, ElementRef, EventEmitter, HostListener, Input, Output, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * A text input with a filterable dropdown of options underneath — used wherever a plain
 * <select> would have too many options to scan (Country / State / City). The value is
 * always one of `options`; typing only filters the list, it never sets a free-text value.
 */
@Component({
  selector: 'app-searchable-select',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './searchable-select.html',
  styleUrl: './searchable-select.css',
})
export class SearchableSelectComponent {
  @Input() options: readonly string[] = [];
  @Input() value = '';
  @Input() placeholder = 'Search...';
  @Input() disabled = false;
  @Input() disabledPlaceholder = 'Not available';
  @Output() valueChange = new EventEmitter<string>();

  protected readonly open = signal(false);
  protected readonly query = signal('');

  constructor(private readonly host: ElementRef<HTMLElement>) {}

  protected readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.options;
    return this.options.filter((o) => o.toLowerCase().includes(q));
  });

  protected get displayValue(): string {
    return this.open() ? this.query() : this.value;
  }

  protected onFocus(): void {
    if (this.disabled) return;
    this.query.set('');
    this.open.set(true);
  }

  protected onInput(value: string): void {
    this.query.set(value);
    this.open.set(true);
  }

  protected select(option: string): void {
    this.valueChange.emit(option);
    this.query.set('');
    this.open.set(false);
  }

  protected clear(event: Event): void {
    event.stopPropagation();
    this.valueChange.emit('');
    this.query.set('');
    this.open.set(false);
  }

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }
}
