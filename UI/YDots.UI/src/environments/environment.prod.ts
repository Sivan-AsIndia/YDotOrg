/**
 * Production environment.
 *
 * `apiBaseUrl` is relative on purpose: nginx serves this bundle and proxies `/api` to the IAM
 * container (see nginx.conf), so the app and the API share one origin. Nothing about the host is
 * baked in, there is no CORS preflight, and the same image runs on any host or port without a
 * rebuild. A same-origin setup also makes the refresh-token cookie a first-party cookie, which
 * browsers treat far more kindly than a third-party one.
 *
 * SMTP settings deliberately do not appear here. See the note in `environment.ts`: mail is sent
 * by the WebAPI, using credentials that never leave the server.
 */
export const environment = {
  production: true,

  apiBaseUrl: '/api/v1',

  /**
   * The other three services, reached through nginx on the SAME origin.
   *
   * Each prefix is rewritten back to /api/... before it is proxied, so the service on the far
   * side sees the path it actually routes on. See the four location blocks in nginx.conf.
   *
   * Same-origin for the same reasons as IAM: no CORS preflight, no host baked into the bundle,
   * and one image that runs on any host or port without a rebuild.
   */
  campaignApiBaseUrl: '/cam-api/v1',

  donorApiBaseUrl: '/don-api/v1',

  /** Unversioned, because the payments service has three surfaces. See environment.ts. */
  paymentApiBaseUrl: '/pay-api',

  applicationName: 'YDot',

  sessionIdleMinutes: 30,

  tokenRefreshLeadSeconds: 120,
};
