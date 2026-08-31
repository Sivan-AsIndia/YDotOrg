import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

export interface SuccessModalData {
  title: string;
  message: string;
  referenceId?: string;
  effectiveTime?: string;
  sessionsNote?: string;
  buttonLabel?: string;
}

@Component({
  selector: 'app-success-modal',
  imports: [CommonModule],
  templateUrl: './success-modal.html',
  styleUrl: './success-modal.css',
})
export class SuccessModalComponent {
  @Input() data: SuccessModalData | null = null;
  @Input() open = false;
  @Output() close = new EventEmitter<void>();
  @Output() action = new EventEmitter<void>();

  onClose(): void {
    this.close.emit();
  }

  onAction(): void {
    this.action.emit();
  }
}