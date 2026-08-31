import { Component, inject, signal, OnInit, OnDestroy, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import QRCode from 'qrcode';
import { ToastService } from '../../../../../Shared/services/toast.service';
import { AuthSessionService } from '../../../../../Shared/services/auth-session.service';
import { SecurityApiService } from '../../../../../Service/security-api.service';
import { AuthTokenService } from '../../../../../Shared/services/auth-token.service';
import { MfaMethodType } from '../../../../../Shared/models/iam-contract.model';
import { MySecurityData } from '../../../../../Shared/models/my-security.model';

type EnrollStep = 'method' | 'setup' | 'success';

interface MfaMethodDef {
  id: string;
  name: string;
  icon: string;
  description: string;
  recommended?: boolean;

  /**
   * Why this method cannot be chosen, when it cannot.
   *
   * Kept in the list rather than removed from it, because "we do not support security keys" and
   * "we have not built security keys yet" are different answers, and somebody looking for the
   * option deserves the second one rather than an absence they have to interpret.
   */
  unavailableReason?: string;
}

@Component({
  selector: 'app-mfa-enrollment',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './mfa-enrollment.html',
  styleUrl: './mfa-enrollment.css',
})
export class MfaEnrollmentComponent implements OnInit, OnDestroy {
  @ViewChild('qrCanvas') qrCanvasRef!: ElementRef<HTMLCanvasElement>;

  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly api = inject(SecurityApiService);
  private readonly auth = inject(AuthTokenService);
  private readonly sessionService = inject(AuthSessionService);

  // ---------- QR display state ----------
  isScanning = signal(false);
  qrScanned = signal(false);
  scannedSecret = signal('');
  qrReady = signal(false);
  private scanTimer: any;

  // ---------- Wizard state ----------
  step = signal<EnrollStep>('method');
  selectedMethod = signal<MfaMethodDef | null>(null);
  deviceName = signal('');

  // ---------- OTP state ----------
  otpDigits = signal<string[]>(['', '', '', '', '', '']);
  isVerifying = signal(false);
  isResending = signal(false);
  otpTimer = signal(120);
  errorMessage = signal('');
  resendMessage = signal('');

  // ---------- Hardware / biometric detection ----------
  isDetecting = signal(false);
  detectionComplete = signal(false);

  // ---------- Success state ----------
  backupCodes = signal<string[]>([]);

  private timerInterval: any;

  readonly methods: MfaMethodDef[] = [
    {
      id: 'authenticator',
      name: 'Authenticator App',
      icon: 'ri-smartphone-line',
      description: 'Scan a QR code with Google Authenticator, Authy or Microsoft Authenticator',
      recommended: true,
    },
    {
      id: 'sms',
      name: 'SMS',
      icon: 'ri-message-3-line',
      description: 'Receive a one-time code on your mobile phone via text message',
    },
    {
      id: 'email',
      name: 'Email OTP',
      icon: 'ri-mail-line',
      description: 'Receive a one-time passcode to your registered email address',
    },
    {
      id: 'security-key',
      name: 'Security Key',
      icon: 'ri-key-2-line',
      description: 'Use a FIDO2 hardware security key (YubiKey, Titan, etc.)',

      // The server refuses this method today. Offering it and failing at the last step wastes
      // somebody's time and teaches them the screen cannot be trusted.
      unavailableReason: 'Not available yet.',
    },
    {
      id: 'biometric',
      name: 'Biometric',
      icon: 'ri-fingerprint-line',
      description: 'Fingerprint or face recognition',

      // Not a factor in its own right: a fingerprint unlocks the DEVICE, and the device then
      // produces one of the factors above. There is nothing here for a server to verify.
      unavailableReason: 'Unlock your device to use your authenticator app instead.',
    },
  ];

  ngOnInit(): void {
    this.startOtpTimer();
  }

  ngOnDestroy(): void {
    this.clearTimer();
    this.clearScanTimer();
  }

  // ==================== Derived values ====================

  /**
   * The factor being enrolled, as the server created it.
   *
   * `sharedSecret` and `provisioningUri` arrive exactly once, when enrolment begins, and are
   * never retrievable again — so they live here for the length of the wizard and nowhere else.
   */
  readonly enrolment = signal<{
    methodId?: string;
    sharedSecret?: string | null;
    provisioningUri?: string | null;
    maskedDestination?: string | null;
    message?: string | null;
  } | null>(null);

  readonly starting = signal(false);

  get personaEmail(): string {
    return this.auth.email();
  }

  get personaPhone(): string {
    return this.enrolment()?.maskedDestination ?? '';
  }

  get maskedEmail(): string {
    const [user, domain] = this.personaEmail.split('@');
    const mask = user.length > 2 ? user.slice(0, 2) + '****' : user + '****';
    return `${mask}@${domain}`;
  }

  get maskedPhone(): string {
    // Masked BY THE SERVER, which knows the number. Masking here would need the real number in
    // the browser, which is the thing masking exists to avoid.
    return this.enrolment()?.maskedDestination ?? '';
  }

  /**
   * The shared secret, for somebody who cannot scan the code.
   *
   * ISSUED BY THE SERVER, once, at the start of enrolment. This was a hard-coded constant, which
   * meant every account in the system shared one secret and any of them could generate a valid
   * code for any other.
   */
  get qrSecret(): string {
    return this.enrolment()?.sharedSecret ?? '';
  }

  get otpCode(): string {
    return this.otpDigits().join('');
  }

  get canVerify(): boolean {
    return this.otpDigits().every((d) => d !== '');
  }

  get formattedOtpTimer(): string {
    const m = Math.floor(this.otpTimer() / 60);
    const s = this.otpTimer() % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  /** Generate a real scannable QR code encoding the otpauth:// URI. */
  async generateRealQrCode(): Promise<void> {
    const canvas = this.qrCanvasRef?.nativeElement;
    if (!canvas) return;

    // The server builds this. Assembling it here would mean the page deciding the algorithm,
    // digit count and period, and any disagreement with the server produces codes that look
    // right and never verify.
    const otpauthUri = this.enrolment()?.provisioningUri;

    if (!otpauthUri) {
      return;
    }

    try {
      await QRCode.toCanvas(canvas, otpauthUri, {
        width: 200,
        margin: 2,
        color: {
          dark: '#1F2430',
          light: '#FFFFFF',
        },
        errorCorrectionLevel: 'M',
      });
      this.qrReady.set(true);
    } catch (err) {
      console.error('QR generation failed', err);
      this.qrReady.set(false);
    }
  }

  // ==================== Wizard navigation ====================

  selectMethod(method: MfaMethodDef): void {
    if (method.unavailableReason) {
      this.toast.show(`${method.name} is not available`, method.unavailableReason, 'info');
      return;
    }

    // Enrolling a factor that already exists is refused by the server, which is the only place
    // that knows what is enrolled. Checking a local copy here would refuse on stale information
    // as often as it caught a genuine duplicate.

    this.selectedMethod.set(method);
    this.errorMessage.set('');
    this.resendMessage.set('');
    this.otpDigits.set(['', '', '', '', '', '']);
    this.detectionComplete.set(false);
    this.isDetecting.set(false);
    this.qrScanned.set(false);
    this.scannedSecret.set('');
    this.step.set('setup');

    this.beginEnrolment(method);
  }

  /**
   * Asks the server to start enrolling this factor.
   *
   * The factor is created PENDING and cannot be used until a code from it is confirmed. That is
   * what stops somebody enrolling a factor they cannot actually reach and locking themselves
   * out of their own account.
   */
  private beginEnrolment(method: { id: string; name: string }): void {
    const methodType = this.toMethodType(method.id);

    if (!methodType) {
      this.errorMessage.set(
        `${method.name} is not available on this platform yet. Choose another method.`);
      this.step.set('method');
      return;
    }

    this.starting.set(true);
    this.enrolment.set(null);
    this.qrReady.set(false);

    this.api.beginMfaEnrolment(methodType, this.deviceName().trim() || undefined).subscribe({
      next: (result) => {
        this.starting.set(false);
        this.enrolment.set(result);

        if (methodType === 'authenticatorApp') {
          void this.generateRealQrCode();
        } else {
          this.resendMessage.set(
            result.message ?? `A verification code has been sent to ${result.maskedDestination}`);
          this.startOtpTimer();
        }
      },
      error: (error: Error) => {
        this.starting.set(false);
        this.errorMessage.set(error.message);
        this.toast.show('Could not start setting that up', error.message, 'error');
        this.step.set('method');
      },
    });
  }

  /** The screen's method ids, in the vocabulary the API uses. */
  private toMethodType(id: string): MfaMethodType | null {
    switch (id) {
      case 'authenticator': return 'authenticatorApp';
      case 'sms': return 'sms';
      case 'email': return 'email';
      case 'security-key': return 'securityKey';

      // Biometric is not a factor the server issues or verifies — a fingerprint unlocks the
      // device, and the device then produces one of the factors above. Offering it as its own
      // choice promised an enrolment nothing could complete.
      default: return null;
    }
  }

  goBack(): void {
    if (this.step() === 'setup') {
      this.clearScanTimer();
      this.step.set('method');
      this.selectedMethod.set(null);
      this.errorMessage.set('');
      this.resendMessage.set('');
      this.clearTimer();
      this.startOtpTimer();
    } else {
      this.router.navigate(['/app/administration/access/my-security']);
    }
  }

  cancel(): void {
    this.clearScanTimer();
    this.router.navigate(['/app/administration/access/my-security']);
  }

  // ==================== Confirming the code was scanned ====================
  //
  // NOTHING HERE WATCHES FOR A SCAN. A web page cannot see somebody point a phone at their own
  // screen, and the previous version's "detected" state was a timer pretending otherwise — it
  // reported success whether or not the code had been scanned at all.
  //
  // The six digits typed on the next step are the proof, and they are the only proof there is:
  // a correct code can only come from an app that holds the secret.

  clearScanTimer(): void {
    if (this.scanTimer) {
      clearTimeout(this.scanTimer);
      this.scanTimer = undefined;
    }
    this.isScanning.set(false);
  }


  // ==================== OTP helpers ====================

  onOtpInput(index: number, value: string): void {
    if (!/^\d*$/.test(value)) {
      this.clearOtpDigit(index);
      return;
    }
    const digits = [...this.otpDigits()];
    digits[index] = value.slice(-1);
    this.otpDigits.set(digits);

    if (value && index < 5) {
      document.querySelector<HTMLInputElement>(`#mfa-otp-${index + 1}`)?.focus();
    }
  }

  private clearOtpDigit(index: number): void {
    const digits = [...this.otpDigits()];
    digits[index] = '';
    this.otpDigits.set(digits);
  }

  onOtpKeydown(index: number, event: KeyboardEvent): void {
    if (event.key === 'Backspace' && !this.otpDigits()[index] && index > 0) {
      document.querySelector<HTMLInputElement>(`#mfa-otp-${index - 1}`)?.focus();
    }
    if (event.key === 'ArrowLeft' && index > 0) {
      document.querySelector<HTMLInputElement>(`#mfa-otp-${index - 1}`)?.focus();
    }
    if (event.key === 'ArrowRight' && index < 5) {
      document.querySelector<HTMLInputElement>(`#mfa-otp-${index + 1}`)?.focus();
    }
  }

  onOtpPaste(event: ClipboardEvent): void {
    const pasted = event.clipboardData?.getData('text') ?? '';
    if (!/^\d{6}$/.test(pasted)) return;
    event.preventDefault();
    this.otpDigits.set(pasted.split(''));
    document.querySelector<HTMLInputElement>('#mfa-otp-5')?.focus();
  }

  startOtpTimer(): void {
    this.clearTimer();
    this.otpTimer.set(120);
    this.timerInterval = setInterval(() => {
      this.otpTimer.update((t) => (t > 0 ? t - 1 : 0));
      if (this.otpTimer() === 0) this.clearTimer();
    }, 1000);
  }

  clearTimer(): void {
    if (this.timerInterval) {
      clearInterval(this.timerInterval);
      this.timerInterval = undefined;
    }
  }

  // ==================== Hardware detection ====================
  //
  // REMOVED, not reimplemented. Detecting a security key means WebAuthn, and WebAuthn produces
  // a signed attestation the server verifies — it is a different enrolment flow from a shared
  // secret and a six-digit code, not a step that can be bolted onto this one. The timer that
  // used to sit here reported a key had been detected without ever looking for one.
  //
  // Security keys are offered as a method; choosing one begins a server-side enrolment like any
  // other, and the code path above is what confirms it.

  // ==================== Verification ====================

  verifyIdentity(): void {
    const method = this.selectedMethod();
    if (!method) return;

    if (!this.deviceName().trim()) {
      this.errorMessage.set('Please enter a device name.');
      this.toast.show('Validation Error', 'Please enter a device name.', 'warning');
      return;
    }

    if (!this.canVerify) {
      this.errorMessage.set('Enter all six digits from your authenticator app.');
      this.toast.show('Check the code', 'Enter all six digits.', 'warning');
      return;
    }

    const methodId = this.enrolment()?.methodId;

    if (!methodId) {
      this.errorMessage.set('This setup has expired. Start again.');
      return;
    }

    this.errorMessage.set('');
    this.isVerifying.set(true);

    // THE SERVER DECIDES. The old version accepted any six digits, which meant somebody could
    // type 000000 and be told two-step verification was protecting their account.
    this.api.confirmMfaEnrolment(methodId, this.otpCode).subscribe({
      next: () => {
        this.isVerifying.set(false);
        this.completeEnrollment();
      },
      error: (error: Error) => {
        this.isVerifying.set(false);
        this.otpDigits.set(['', '', '', '', '', '']);
        this.errorMessage.set(error.message);
        this.toast.show('That code did not work', error.message, 'error');
      },
    });
  }

  /**
   * Sends another code.
   *
   * Starting enrolment again is what issues one: the server retires the outstanding challenge
   * as it mints the new one, so only the newest code works. A "resend" that reused the old
   * challenge would leave two live codes, and two live codes is one more than intended.
   */
  resendCode(): void {
    const method = this.selectedMethod();

    if (!method) {
      return;
    }

    this.isResending.set(true);
    this.errorMessage.set('');

    const methodType = this.toMethodType(method.id);

    if (!methodType) {
      this.isResending.set(false);
      return;
    }

    this.api.beginMfaEnrolment(methodType, this.deviceName().trim() || undefined).subscribe({
      next: (result) => {
        this.isResending.set(false);
        this.enrolment.set(result);
        this.otpDigits.set(['', '', '', '', '', '']);
        this.startOtpTimer();
        this.resendMessage.set(
          result.message ?? `A new code has been sent to ${result.maskedDestination}`);
      },
      error: (error: Error) => {
        this.isResending.set(false);
        this.errorMessage.set(error.message);
        this.toast.show('Could not send a new code', error.message, 'error');
      },
    });
  }

  // ==================== Finishing up ====================

  /**
   * Finishes up: the factor is confirmed, so fetch the backup codes and show them.
   *
   * THE CODES COME FROM THE SERVER. They were generated in the browser with `Math.random()`,
   * which is not a source of randomness anybody should protect an account with, and they were
   * never sent anywhere — so they would not have worked. Real codes are minted server-side,
   * stored as hashes, and returned exactly once, here.
   *
   * NOTHING IS WRITTEN INTO A CACHE. This used to patch three in-memory stores and
   * localStorage so other screens would show "Enrolled"; those screens now re-read from the
   * server, which is the only copy that can be right.
   */
  private completeEnrollment(): void {
    this.api.generateRecoveryCodes().subscribe({
      next: (result) => {
        this.backupCodes.set(result.codes ?? []);
        this.step.set('success');
        this.toast.show('Two-step verification is on',
          'Save your backup codes before you close this page.', 'success');
      },
      error: () => {
        // The factor IS enrolled — that call already succeeded. Only the codes failed, and
        // saying so is better than implying the enrolment did not happen.
        this.backupCodes.set([]);
        this.step.set('success');
        this.toast.show('Two-step verification is on',
          'Your backup codes could not be generated. Generate them from the security page.',
          'warning');
      },
    });
  }

  finish(): void {
    // Enrolling a second factor from inside the app does not start a session — one is already
    // running, which is how this screen was reached. The old code called startSession() here
    // with a hand-built object, which would now overwrite the real signed-in identity with a
    // partial copy read out of localStorage. Just go back to the dashboard.
    this.router.navigate(['/app/dashboard']);
  }
}