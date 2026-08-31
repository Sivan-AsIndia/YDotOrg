import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmDialogConfig } from '../../models/donors-leads.model';

/**
 * Shared confirmation modal for Donors and Leads screens.
 * Used by SCR-DON-001 … SCR-DON-006 + DON-UI-07 + DON-UI-08.
 * Supports reason capture, typed confirmation and before/after review.
 */
@Component({
  selector: 'app-confirm-modal',
  imports: [CommonModule, FormsModule],
  templateUrl: './confirm-modal.html',
  styleUrl: './confirm-modal.css',
})
export class ConfirmModalComponent {
  @Input() config: ConfirmDialogConfig | null = null;
  @Output() confirm = new EventEmitter<string>();
  @Output() cancel = new EventEmitter<void>();

  protected readonly reason = signal('');
  protected readonly typedValue = signal('');
  protected readonly touched = signal(false);

  protected get reasonValid(): boolean {
    const cfg = this.config;
    if (!cfg?.requireReason) {
      return true;
    }
    const len = this.reason().trim().length;
    const min = cfg.reasonMin ?? 10;
    const max = cfg.reasonMax ?? 2000;
    return len >= min && len <= max;
  }

  protected get typedValid(): boolean {
    const cfg = this.config;
    if (!cfg?.typedConfirm) {
      return true;
    }
    return this.typedValue().trim().toLowerCase() === cfg.confirmLabel.toLowerCase();
  }

  protected get canConfirm(): boolean {
    return this.reasonValid && this.typedValid;
  }

  protected get reasonCount(): number {
    return this.reason().trim().length;
  }

  protected get reasonMax(): number {
    return this.config?.reasonMax ?? 2000;
  }

  protected get reasonMin(): number {
    return this.config?.reasonMin ?? 10;
  }

  protected onReasonInput(value: string): void {
    this.reason.set(value);
  }

  protected onTypedInput(value: string): void {
    this.typedValue.set(value);
  }

  protected onBlurReason(): void {
    this.touched.set(true);
  }

  protected confirmAction(): void {
    if (!this.canConfirm) {
      this.touched.set(true);
      return;
    }
    this.confirm.emit(this.reason().trim());
  }

  protected cancelAction(): void {
    this.cancel.emit();
  }
}