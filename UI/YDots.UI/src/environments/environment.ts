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
   * RELATIVE, NOT http://localhost:6702, AND THAT IS THE WHOLE POINT. `ng serve` proxies these
   * prefixes to the four APIs (see proxy.conf.json), exactly as nginx does in the container. So
   * the browser makes every call to its OWN origin.
   *
   * WHY IT HAS TO BE THIS WAY. IAM picks the Organisation from the Host header of the API
   * request: `localhost` is the PLATFORM host and only the SuperAdmin is visible there, while
   * `ten1.localhost` is an Organisation host. With an absolute URL the page could be on
   * ten1.localhost:6701 and the call would still arrive as `localhost` - so an Organisation user
   * signing in with a perfectly good password was told the details were incorrect. Proxied, the
   * Host travels with the request and the Organisation resolves.
   *
   * Two things come free with same-origin: no CORS preflight, and the refresh cookie is a
   * first-party cookie, which browsers treat far more kindly. Production works the same way -
   * see environment.prod.ts.
   */
  apiBaseUrl: '/api/v1',

  /**
   * The other three services, reached through the dev-server proxy on the SAME origin.
   *
   * WHY FOUR BASE URLS AND NOT ONE. The platform is four ASP.NET services sharing one
   * PostgreSQL database, not one API. IAM signs the token; CAM, DON and PAY only validate it.
   * A single base URL would mean one service answering for endpoints it does not own, which is
   * exactly the coupling the split was meant to avoid.
   *
   * Each prefix is rewritten back to /api/... before it is forwarded, so the service on the far
   * side sees the path it actually routes on and knows nothing about the prefix. The rewrites in
   * proxy.conf.json are the same ones nginx.conf performs in the container.
   */
  campaignApiBaseUrl: '/cam-api/v1',

  donorApiBaseUrl: '/don-api/v1',

  /**
   * NOTE THE MISSING /v1. The payments service has three surfaces and only one of them is
   * versioned: /api/v1/* for staff endpoints, /api/public/* for the donor flow, and
   * /api/webhooks/* for the gateway. The service appends the right segment itself.
   */
  paymentApiBaseUrl: '/pay-api',

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
