import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response.model';
import { ApiEnumOption, toApiEnumOptions } from '../models/enum-option.model';
import { EnumOptionsResponse } from '../models/iam-contract.model';

/** The option sets whose values are enum members - everything except the stored-text lists. */
export type EnumOptionSet = Exclude<keyof EnumOptionsResponse, 'organisationTypes'>;

/**
 * Every enumeration the administration screens render as a dropdown, from
 * `GET /api/v1/reference-data/enums`.
 *
 * WHAT THIS REPLACES. The organisation directory, the department editor, the menu catalogue and
 * the audit trail each carried their own literal list of statuses, levels and outcomes. A literal
 * list is wrong the day the server adds a value, and silently so: the new value can never be
 * chosen, and a record already carrying it renders a blank select.
 *
 * CACHED FOR THE LIFE OF THE APPLICATION. The enums change with a deployment, never between two
 * page views, so the second screen that asks costs nothing. `shareReplay` resets on error, so a
 * failed fetch is retried by the next caller rather than remembered.
 */
@Injectable({ providedIn: 'root' })
export class EnumOptionsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/reference-data/enums`;

  private enums$?: Observable<EnumOptionsResponse>;

  getAll(): Observable<EnumOptionsResponse> {
    this.enums$ ??= this.http.get<ApiResponse<EnumOptionsResponse>>(this.url).pipe(
      map((response) => response.data ?? {}),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.enums$;
  }

  /** One enumeration, with its values in the camelCase every API record uses. */
  options(name: EnumOptionSet): Observable<ApiEnumOption[]> {
    return this.getAll().pipe(map((all) => toApiEnumOptions(all[name])));
  }

  /**
   * The organisation types, values UNCHANGED.
   *
   * These are labels stored verbatim in a string column, not enum members, so they are passed
   * through exactly as the server sent them.
   */
  organisationTypes(): Observable<string[]> {
    return this.getAll().pipe(
      map((all) => (all.organisationTypes ?? [])
        .map((option) => option.value ?? '')
        .filter((value) => !!value)),
    );
  }
}
