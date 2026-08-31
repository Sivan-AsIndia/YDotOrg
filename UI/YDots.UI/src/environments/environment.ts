/**
 * Development environment.
 *
 * WHAT IS *NOT* HERE ANY MORE, AND WHY
 * ------------------------------------
 * This file used to carry the SMTP host, username and password. Everything in an Angular
 * environment file is compiled into the JavaScript bundle that every visitor downloads, so those
 * credentials were readable by anyone who opened the browser's dev tools — and could then be used
 * to send mail as this organisation.
 *
 * All e-mail is now sent by the WebAPI. The credentials live in the API's `appsettings.json`
 * under `EmailSettings`, where the browser cannot reach them. The UI never sends mail itself; it
 * calls an endpoint (forgot password, invite user, …) and the server does the rest.
 *
 * Rule of thumb: if a value would be dangerous in the hands of a stranger, it does not belong in
 * an environment file.
 */
export const environment = {
  production: false,

  /**
   * Root of the IAM WebAPI. Every service builds its URLs from this.
   *
   * Plain HTTP on purpose in development: the API's dev profile turns HTTPS redirection off, and
   * the refresh cookie is issued with Secure=false, because a Secure cookie is simply not stored
   * by a browser on an http:// origin — the refresh flow would fail with nothing in the console
   * to explain why. Production is same-origin over HTTPS; see environment.prod.ts.
   */
  apiBaseUrl: 'http://localhost:6702/api/v1',

  /**
   * The other three services, each on its own port in development.
   *
   * WHY FOUR BASE URLS AND NOT ONE. The platform is four ASP.NET services sharing one
   * PostgreSQL database, not one API. IAM signs the token; CAM, DON and PAY only validate it.
   * A single base URL would mean one service answering for endpoints it does not own, which is
   * exactly the coupling the split was meant to avoid.
   *
   * IN PRODUCTION ALL FOUR ARE SAME-ORIGIN PATHS behind nginx - see environment.prod.ts. The
   * ports below exist only because `ng serve` runs on 4200 and each API runs on its own port,
   * so the browser genuinely is talking to four origins. Each API's CORS list names
   * http://localhost:6701 for that reason.
   */
  campaignApiBaseUrl: 'http://localhost:6704/api/v1',

  donorApiBaseUrl: 'http://localhost:6706/api/v1',

  /**
   * NOTE THE MISSING /v1. The payments service has three surfaces and only one of them is
   * versioned: /api/v1/* for staff endpoints, /api/public/* for the donor flow, and
   * /api/webhooks/* for the gateway. The service appends the right segment itself.
   */
  paymentApiBaseUrl: 'http://localhost:6708/api',

  /** Shown in page titles, e-mails and the authenticator app entry. */
  applicationName: 'YDot',

  /**
   * How long the browser may sit idle before the app asks the person to confirm it is still
   * them. Keep this at or below the API's `SecuritySettings.SessionIdleTimeoutMinutes`.
   */
  sessionIdleMinutes: 30,

  /**
   * How many seconds before the access token expires that the app quietly renews it.
   * The access token lives about 15 minutes, so 120 seconds of headroom is comfortable.
   */
  tokenRefreshLeadSeconds: 120,
};
