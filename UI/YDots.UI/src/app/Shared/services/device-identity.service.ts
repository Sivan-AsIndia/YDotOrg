import { Injectable } from '@angular/core';

/**
 * Gives this browser a stable, anonymous id so "remember this device for 30 days" can work.
 *
 * The value is a random UUID with no personal information in it, and the server stores only its
 * SHA-256 hash. So the id proves "this is the same browser that was trusted before" and nothing
 * else — a leaked database cannot be turned back into a list of devices or people.
 *
 * It lives in `localStorage`, not `sessionStorage`, because the whole point is that it survives
 * closing the tab. Clearing site data forgets the device, which simply means the next sign-in
 * asks for a second factor again. That is the correct, safe failure.
 */
@Injectable({ providedIn: 'root' })
export class DeviceIdentityService {
  private static readonly DEVICE_ID_KEY = 'ydot.deviceId';

  /** The stable id for this browser, created on first use. */
  getDeviceIdentifier(): string {
    let id = localStorage.getItem(DeviceIdentityService.DEVICE_ID_KEY);

    if (!id) {
      id = this.createId();
      localStorage.setItem(DeviceIdentityService.DEVICE_ID_KEY, id);
    }

    return id;
  }

  /**
   * A human-readable label so the security screen can list "Chrome on Windows" rather than a
   * raw user-agent string. Best-effort only: it is a convenience, never a security control.
   */
  getDeviceName(): string {
    const agent = navigator.userAgent;

    const browser =
      /Edg\//.test(agent) ? 'Edge'
      : /OPR\//.test(agent) ? 'Opera'
      : /Chrome\//.test(agent) ? 'Chrome'
      : /Firefox\//.test(agent) ? 'Firefox'
      : /Safari\//.test(agent) ? 'Safari'
      : 'Browser';

    const platform =
      /Windows/.test(agent) ? 'Windows'
      : /Android/.test(agent) ? 'Android'
      : /iPhone|iPad|iPod/.test(agent) ? 'iOS'
      : /Mac OS X/.test(agent) ? 'macOS'
      : /Linux/.test(agent) ? 'Linux'
      : 'Unknown OS';

    return `${browser} on ${platform}`;
  }

  /** Forgets this device, so the next sign-in is challenged again. */
  forget(): void {
    localStorage.removeItem(DeviceIdentityService.DEVICE_ID_KEY);
  }

  private createId(): string {
    // crypto.randomUUID needs a secure context; over plain HTTP it is undefined, so fall back to
    // getRandomValues, which is available everywhere this app runs.
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  }
}
