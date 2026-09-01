import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
} from '@angular/router';
import { Observable, Subject, isObservable, of, throwError } from 'rxjs';
import { AuthApiService } from '../../Service/auth-api.service';
import {
  PersonLookupResponse,
  UserDirectoryApiService,
} from '../../Service/user-directory-api.service';
import { platformScopeGuard } from '../guards/permission.guard';
import {
  AuthenticatedUserResponse,
  NavigationResponse,
  SelectTenantResponse,
  TenantContextResponse,
} from '../models/auth.model';
import { AuthTokenService } from './auth-token.service';
import { NavigationService } from './navigation.service';
import { OrganisationContextService } from './organisation-context.service';
import { OrganisationScopeService } from './organisation-scope.service';
import { PeopleDirectoryService } from './people-directory.service';

/**
 * Organisation isolation.
 *
 * THE BUG THESE COVER. A SuperAdmin steps into TEN001, walks back to Manage Organisations, and the
 * TenantAdmin sidebar is still there. Each test below is one of the paths that produced it, so a
 * regression fails here rather than being found by somebody looking at the wrong menu.
 */

const SUPER_ADMIN: AuthenticatedUserResponse = {
  id: 'u-1',
  displayName: 'Root',
  email: 'root@example.test',
  isSuperAdmin: true,
  roles: [],
  permissions: [],
};

function tenantContext(id: string | null, name: string): TenantContextResponse {
  return { tenantId: id, tenantName: name, isTenantMode: id !== null };
}

function navigationFor(name: string): NavigationResponse {
  return {
    menu: [{ id: 'm-' + name, code: 'MENU_' + name, name } as never],
    landingRoute: '/app/' + name.toLowerCase(),
    tenantName: name,
  };
}

/**
 * Stands in for the API.
 *
 * `selectOrganisation` stores the new context exactly as the real one does, because that store is
 * what everything under test reacts to - a fake that skipped it would be testing nothing.
 */
class FakeAuthApi {
  navigationCalls = 0;
  exitCalls = 0;
  failNavigation = false;

  /** The Organisation the next navigation call should describe. */
  currentName = 'PLATFORM';

  constructor(private readonly tokens: AuthTokenService) {}

  getNavigation(): Observable<NavigationResponse> {
    this.navigationCalls++;

    return this.failNavigation
      ? throwError(() => new Error('navigation unavailable'))
      : of(navigationFor(this.currentName));
  }

  selectOrganisation(tenantId: string): Observable<SelectTenantResponse> {
    this.currentName = tenantId;
    const response: SelectTenantResponse = { tenant: tenantContext(tenantId, tenantId) };
    this.tokens.storeTenantSelection(response);

    return of(response);
  }

  exitOrganisation(): Observable<SelectTenantResponse> {
    this.exitCalls++;
    this.currentName = 'PLATFORM';
    const response: SelectTenantResponse = { tenant: tenantContext(null, '') };
    this.tokens.storeTenantSelection(response);

    return of(response);
  }
}

/** Stands in for the user directory, which is Organisation-scoped server-side. */
class FakePeopleApi {
  calls = 0;

  /** The people the next call should return, so a test can prove the list changed. */
  people: PersonLookupResponse[] = [{ id: 'p-platform', displayName: 'Platform Person' }];

  peopleDirectory(): Observable<PersonLookupResponse[]> {
    this.calls++;

    return of(this.people);
  }
}

describe('organisation isolation', () => {
  let tokens: AuthTokenService;
  let api: FakeAuthApi;
  let peopleApi: FakePeopleApi;

  beforeEach(() => {
    sessionStorage.clear();
    peopleApi = new FakePeopleApi();

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthApiService, useFactory: () => api },
        { provide: UserDirectoryApiService, useFactory: () => peopleApi },
      ],
    });

    tokens = TestBed.inject(AuthTokenService);
    api = new FakeAuthApi(tokens);

    // Signed in as SuperAdmin, standing outside every Organisation.
    tokens.storeUser(SUPER_ADMIN);
    tokens.storeTenant(tenantContext(null, ''));
  });

  describe('OrganisationScopeService', () => {
    it('reports platform scope outside an organisation and the id inside one', () => {
      const scope = TestBed.inject(OrganisationScopeService);

      expect(scope.scope()).toBe(OrganisationScopeService.PLATFORM);
      expect(scope.organisationId()).toBeNull();

      tokens.storeTenant(tenantContext('TEN001', 'One'));

      expect(scope.scope()).toBe('TEN001');
      expect(scope.organisationId()).toBe('TEN001');
    });

    it('announces a change of organisation, and only a change', () => {
      const scope = TestBed.inject(OrganisationScopeService);
      const seen: (string | null)[] = [];
      scope.onOrganisationChange((next) => seen.push(next));

      TestBed.tick();
      expect(seen).toEqual([]);

      tokens.storeTenant(tenantContext('TEN001', 'One'));
      TestBed.tick();
      expect(seen).toEqual(['TEN001']);

      // The same Organisation stored again is not a change, even though the object is a new one.
      tokens.storeTenant(tenantContext('TEN001', 'One'));
      TestBed.tick();
      expect(seen).toEqual(['TEN001']);

      tokens.storeTenant(tenantContext('TEN002', 'Two'));
      TestBed.tick();
      expect(seen).toEqual(['TEN001', 'TEN002']);
    });

    it('reports null when the session ends, so caches discard rather than re-fetch', () => {
      const scope = TestBed.inject(OrganisationScopeService);
      const seen: (string | null)[] = [];
      scope.onOrganisationChange((next) => seen.push(next));

      tokens.clear();
      TestBed.tick();

      expect(seen).toEqual([null]);
    });
  });

  describe('NavigationService', () => {
    it('loads the tree for the scope it starts in', () => {
      const navigation = TestBed.inject(NavigationService);

      expect(api.navigationCalls).toBe(1);
      expect(navigation.organisationName()).toBe('PLATFORM');
    });

    it('drops the previous organisation menu and reloads when the organisation changes', () => {
      const navigation = TestBed.inject(NavigationService);
      expect(navigation.menu().length).toBe(1);

      api.currentName = 'TEN001';
      tokens.storeTenant(tenantContext('TEN001', 'One'));
      TestBed.tick();

      expect(api.navigationCalls).toBe(2);
      expect(navigation.organisationName()).toBe('TEN001');
    });

    it('ignores a menu that arrives after the organisation it belongs to has been left', () => {
      const navigation = TestBed.inject(NavigationService);

      // The reply for the Organisation we are about to leave, held open.
      const slow = new Subject<NavigationResponse>();
      api.getNavigation = () => slow.asObservable();

      tokens.storeTenant(tenantContext('TEN001', 'One'));
      TestBed.tick();

      // Left again before that reply lands, and the platform tree comes back first.
      api.getNavigation = () => of(navigationFor('PLATFORM'));
      tokens.storeTenant(tenantContext(null, ''));
      TestBed.tick();
      expect(navigation.organisationName()).toBe('PLATFORM');

      // TEN001's reply now arrives late. It must not overwrite the platform menu.
      slow.next(navigationFor('TEN001'));
      expect(navigation.organisationName()).toBe('PLATFORM');
    });

    it('forgets the tree on sign-out rather than leaving it for the next person', () => {
      const navigation = TestBed.inject(NavigationService);
      expect(navigation.menu().length).toBe(1);

      tokens.clear();
      TestBed.tick();

      expect(navigation.menu()).toEqual([]);
      expect(api.navigationCalls).toBe(1);
    });
  });

  describe('OrganisationContextService', () => {
    it('does not complete a switch until the new organisation menu is in hand', () => {
      const navigation = TestBed.inject(NavigationService);
      const organisations = TestBed.inject(OrganisationContextService);

      let landingAtCompletion = '';
      organisations.select('TEN001').subscribe(() => {
        landingAtCompletion = navigation.landingRoute();
      });

      expect(landingAtCompletion).toBe('/app/ten001');
      expect(navigation.organisationName()).toBe('TEN001');
    });

    it('fetches the menu once per switch, not once per interested party', () => {
      TestBed.inject(NavigationService);
      const organisations = TestBed.inject(OrganisationContextService);
      const callsBefore = api.navigationCalls;

      organisations.select('TEN001').subscribe();
      TestBed.tick();

      expect(api.navigationCalls).toBe(callsBefore + 1);
    });

    it('switches between two organisations, loading each of their menus', () => {
      const navigation = TestBed.inject(NavigationService);
      const organisations = TestBed.inject(OrganisationContextService);

      organisations.select('TEN001').subscribe();
      TestBed.tick();
      expect(navigation.organisationName()).toBe('TEN001');
      expect(navigation.landingRoute()).toBe('/app/ten001');

      organisations.select('TEN002').subscribe();
      TestBed.tick();
      expect(navigation.organisationName()).toBe('TEN002');
      expect(navigation.landingRoute()).toBe('/app/ten002');
    });

    it('leaves the organisation and comes back with the platform menu', () => {
      const navigation = TestBed.inject(NavigationService);
      const organisations = TestBed.inject(OrganisationContextService);

      organisations.select('TEN001').subscribe();
      TestBed.tick();
      expect(tokens.isActingInOrganisation()).toBe(true);

      organisations.exitToPlatform().subscribe();
      TestBed.tick();

      expect(tokens.isActingInOrganisation()).toBe(false);
      expect(navigation.organisationName()).toBe('PLATFORM');
    });

    it('still completes the switch when the menu cannot be fetched', () => {
      TestBed.inject(NavigationService);
      const organisations = TestBed.inject(OrganisationContextService);
      api.failNavigation = true;

      let completed = false;
      let errored = false;
      organisations.select('TEN001').subscribe({
        next: () => (completed = true),
        error: () => (errored = true),
      });

      // The token changed, so the switch happened. An error here would say otherwise.
      expect(completed).toBe(true);
      expect(errored).toBe(false);
      expect(tokens.organisationName()).toBe('TEN001');
    });
  });

  describe('platformScopeGuard', () => {
    const route = {} as ActivatedRouteSnapshot;
    const state = { url: '/app/administration/organisation/directory' } as RouterStateSnapshot;

    /** Runs the guard and settles whichever of the two shapes a CanActivateFn may return. */
    function activate(): boolean {
      const result = TestBed.runInInjectionContext(() => platformScopeGuard(route, state));

      if (isObservable(result)) {
        let allowed = false;
        result.subscribe((value) => (allowed = value as boolean));

        return allowed;
      }

      return result as boolean;
    }

    it('does nothing when the session is already at platform level', () => {
      TestBed.inject(NavigationService);

      expect(activate()).toBe(true);
      expect(api.exitCalls).toBe(0);
    });

    it('leaves the organisation on the way in, so the platform menu comes back', () => {
      const navigation = TestBed.inject(NavigationService);
      const organisations = TestBed.inject(OrganisationContextService);

      organisations.select('TEN001').subscribe();
      TestBed.tick();
      expect(navigation.organisationName()).toBe('TEN001');

      // This is the reported bug: walking back to Manage Organisations from inside TEN001.
      expect(activate()).toBe(true);
      expect(api.exitCalls).toBe(1);
      expect(tokens.isActingInOrganisation()).toBe(false);
      expect(navigation.organisationName()).toBe('PLATFORM');
    });

    it('lets the person through when leaving fails, rather than stranding them', () => {
      TestBed.inject(NavigationService);
      const organisations = TestBed.inject(OrganisationContextService);

      organisations.select('TEN001').subscribe();
      TestBed.tick();

      api.exitOrganisation = () => throwError(() => new Error('offline'));

      expect(activate()).toBe(true);
    });
  });

  describe('organisation-scoped caches', () => {
    it('discards and reloads the people directory when the organisation changes', () => {
      const people = TestBed.inject(PeopleDirectoryService);
      expect(peopleApi.calls).toBe(1);
      expect(people.all()[0].name).toBe('Platform Person');

      peopleApi.people = [{ id: 'p-ten001', displayName: 'TEN001 Person' }];
      tokens.storeTenant(tenantContext('TEN001', 'One'));
      TestBed.tick();

      expect(peopleApi.calls).toBe(2);
      expect(people.all()[0].name).toBe('TEN001 Person');
    });

    it('empties the cache before the replacement arrives, never showing the previous one', () => {
      const people = TestBed.inject(PeopleDirectoryService);
      expect(people.all().length).toBe(1);

      // A request that never answers, so the only thing on screen is what the reset left behind.
      peopleApi.peopleDirectory = () => new Observable<PersonLookupResponse[]>();

      tokens.storeTenant(tenantContext('TEN001', 'One'));
      TestBed.tick();

      expect(people.all()).toEqual([]);
    });
  });
});
