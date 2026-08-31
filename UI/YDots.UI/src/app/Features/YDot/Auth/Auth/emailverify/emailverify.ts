import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { ToastService } from '../../../../../Shared/services/toast.service';

@Component({
  selector: 'app-emailverify',
  imports: [FormsModule, CommonModule, RouterModule],
  templateUrl: './emailverify.html',
  styleUrl: './emailverify.css',
})
export class EmailverifyComponent {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  otpDigits: string[] = ['', '', '', '', ''];
  isSubmitting: boolean = false;
  isResending: boolean = false;
  codeExpirySeconds: number = 120;
  codeExpiryTimer: any;
  errorMessage: string = '';
  successMessage: string = '';

  ngOnInit(): void {
    this.startCodeExpiryTimer();
  }

  ngOnDestroy(): void {
    if (this.codeExpiryTimer) clearInterval(this.codeExpiryTimer);
  }

  get otpCode(): string {
    return this.otpDigits.join('');
  }

  get canVerify(): boolean {
    return this.otpDigits.every((d) => d !== '');
  }

  get formattedCodeExpiry(): string {
    const m = Math.floor(this.codeExpirySeconds / 60);
    const s = this.codeExpirySeconds % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  startCodeExpiryTimer(): void {
    if (this.codeExpiryTimer) clearInterval(this.codeExpiryTimer);
    this.codeExpirySeconds = 120;
    this.codeExpiryTimer = setInterval(() => {
      if (this.codeExpirySeconds > 0) this.codeExpirySeconds--;
      else clearInterval(this.codeExpiryTimer);
    }, 1000);
  }

  onOtpInput(index: number, value: string): void {
    if (!/^\d*$/.test(value)) {
      this.otpDigits[index] = '';
      return;
    }
    this.otpDigits[index] = value.slice(-1);
    if (value && index < 4) {
      document.querySelector<HTMLInputElement>(`#email-otp-${index + 1}`)?.focus();
    }
  }

  onOtpKeydown(index: number, event: KeyboardEvent): void {
    if (event.key === 'Backspace' && !this.otpDigits[index] && index > 0) {
      document.querySelector<HTMLInputElement>(`#email-otp-${index - 1}`)?.focus();
    }
  }

  onOtpPaste(event: ClipboardEvent): void {
    const pasted = event.clipboardData?.getData('text') ?? '';
    if (!/^\d{4}$/.test(pasted)) return;
    event.preventDefault();
    this.otpDigits = pasted.split('');
    document.querySelector<HTMLInputElement>('#email-otp-3')?.focus();
  }

  verifyEmail(): void {
    this.errorMessage = '';
    this.successMessage = '';

    if (!this.canVerify) {
      this.errorMessage = 'Please enter the full 4-digit verification code.';
      this.toast.show('Validation Error', 'Please enter the full 4-digit verification code.', 'warning');
      return;
    }

    this.isSubmitting = true;
    setTimeout(() => {
      this.isSubmitting = false;
      if (/^\d{4}$/.test(this.otpCode)) {
        this.successMessage = 'Email verified. Redirecting to reset password...';
        this.toast.show('Email Verified', 'Your email has been verified successfully.', 'success');
        setTimeout(() => {
          this.router.navigate(['/auth/reset-password']);
        }, 1000);
      } else {
        this.errorMessage = 'Invalid verification code. Please try again.';
        this.toast.show('Verification Failed', 'Invalid verification code. Please try again.', 'error');
      }
    }, 1500);
  }

  resendCode(): void {
    this.isResending = true;
    this.errorMessage = '';
    this.successMessage = '';
    setTimeout(() => {
      this.isResending = false;
      this.startCodeExpiryTimer();
      this.successMessage = 'A new verification code has been sent to your email.';
    }, 1500);
  }
}