import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { authInterceptor } from './Shared/interceptors/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // withComponentInputBinding lets a route parameter such as :token arrive as an @Input on the
    // component, so screens do not each hand-roll ActivatedRoute plumbing.
    provideRouter(routes, withComponentInputBinding()),

    // withInterceptors registers authInterceptor, which attaches the bearer token, turns on
    // withCredentials so the HttpOnly refresh cookie is sent, renews an expired access token
    // once and replays the request, and unwraps the API's error envelope.
    //
    // withFetch uses the Fetch API instead of XMLHttpRequest, which is the modern default and
    // handles credentialed cross-origin requests more predictably.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
  ],
};
