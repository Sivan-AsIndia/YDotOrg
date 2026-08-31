import { Injectable, computed, inject, signal } from '@angular/core';
import { UserDirectoryApiService } from '../../Service/user-directory-api.service';
import { UserSearchFilter } from '../models/user-directory.model';

/**
 * One option in an owner, approver or assignee selector.
 *
 * `reference` IS THE API ID, not the human code. Every endpoint that takes a person takes the id;
 * the code is what a person quotes. Selectors that stored the code had to translate it back on the
 * way out, and two of them translated it wrongly.
 */
export interface PersonOption {
  /** The API id - what a request carries. */
  readonly reference: string;
  /** The human reference - USR-000184 - which is what somebody reads off a screen. */
  readonly code: string;
  readonly name: string;
  /** Role and unit, for telling two people with the same name apart. */
  readonly context: string;
  readonly isActive: boolean;
  readonly email?: string;
  readonly avatarUrl?: string;

  /** Two letters for an avatar, derived from the name rather than stored. */
  readonly initials: string;

  /**
   * A stable colour for the avatar.
   *
   * DERIVED FROM THE ID, so one person is the same colour on every screen and across reloads.
   * Assigning tones by list position - which the hard-coded lists effectively did - meant somebody
   * changed colour as soon as anybody was added above them.
   */
  readonly tone: string;
}

/** The avatar palette, in the order a stable hash indexes it. */
const AVATAR_TONES = ['meadow', 'gold', 'blue', 'plum', 'coral', 'teal'] as const;

/**
 * The people who can own, approve or be assigned things.
 *
 * WHY THIS EXISTS. Six screens each carried their own copy of the same five invented people -
 * 'USR-0114 · Arun Kumar', 'USR-0099 · Sophie Bennett' and so on - as a hard-coded map. Every one
 * of those copies had the same three problems:
 *
 *   - THE PEOPLE DID NOT EXIST. Assigning a campaign to 'USR-0114' assigned it to nobody, and the
 *     screen then displayed 'Arun Kumar' as the accountable owner of that campaign. A blocker
 *     owned by a person who does not exist is a blocker nobody is working on.
 *   - EVERY ORGANISATION SAW THE SAME FIVE NAMES, because a constant in a bundle does not know who
 *     is asking. One charity's screen offered another charity's staff.
 *   - THE COPIES DISAGREED. Two of the six listed people the other four did not, so the name shown
 *     against an owner depended on which screen you were looking at.
 *
 * IT NOW READS THE IAM USER DIRECTORY, which is organisation-scoped server-side: a caller sees the
 * people in their own data scope and nobody else's.
 *
 * THE SURFACE IS SYNCHRONOUS because these are selector options and name lookups read from
 * templates. The load happens once on first injection and the signal fills in; a name asked for
 * before it arrives comes back as the reference itself, which is honest - it shows an unresolved
 * id rather than inventing a person to go with it.
 */
@Injectable({ providedIn: 'root' })
export class PeopleDirectoryService {
  private readonly api = inject(UserDirectoryApiService);

  private readonly people = signal<readonly PersonOption[]>([]);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  /** Everybody in the caller's data scope, active first and then by name. */
  readonly all = computed(() => this.people());

  /**
   * The people who may be given something to own.
   *
   * ACTIVE ONLY. Assigning work to a suspended or withdrawn account is how a blocker sits
   * untouched for a fortnight: the owner is named, so it looks assigned, and nobody is reading it.
   */
  readonly assignable = computed(() => this.people().filter((person) => person.isActive));

  constructor() {
    this.refresh();
  }

  refresh(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    // THE PICKER ENDPOINT, not the administration search. See peopleDirectory() for why.
    this.api.peopleDirectory().subscribe({
      next: (items) => {
        this.people.set(
          items
            .filter((person) => !!person.id)
            .map((person) => ({
              reference: person.id,
              code: person.code ?? '',
              name: person.displayName || person.code || person.id,
              context: person.code ?? '',

              // The endpoint returns ACTIVE people only, so everyone it names can be given work.
              isActive: true,
              email: undefined,
              avatarUrl: undefined,
              initials: initialsOf(person.displayName || person.code || ''),
              tone: toneFor(person.id),
            }))
            .sort((left, right) => {
              if (left.isActive !== right.isActive) {
                return left.isActive ? -1 : 1;
              }

              return left.name.localeCompare(right.name);
            }),
        );

        this.isLoading.set(false);
      },
      error: () => {
        this.people.set([]);
        this.isLoading.set(false);
        this.loadError.set('The people directory could not be loaded.');
      },
    });
  }

  /**
   * The display name for a person.
   *
   * IT FALLS BACK TO THE REFERENCE ITSELF rather than to a placeholder name. An unresolved id
   * shown as an id is a visible loose end somebody can chase; the same id shown as 'Arun Kumar'
   * is a wrong answer nobody will ever question.
   */
  name(reference: string | null | undefined): string {
    if (!reference) {
      return 'Unassigned';
    }

    const match = this.people().find(
      (person) => person.reference === reference || person.code === reference,
    );

    return match?.name ?? reference;
  }

  /** One person by id or by human reference. */
  get(reference: string | null | undefined): PersonOption | undefined {
    if (!reference) {
      return undefined;
    }

    return this.people().find(
      (person) => person.reference === reference || person.code === reference,
    );
  }

  /** The API id behind a human reference, for a screen still holding codes. */
  idOf(reference: string | null | undefined): string | undefined {
    return this.get(reference)?.reference;
  }
}

/** Two letters from a display name. '??' when there is nothing to take them from. */
function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);

  if (parts.length === 0) {
    return '??';
  }

  if (parts.length === 1) {
    return parts[0].slice(0, 2).toUpperCase();
  }

  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

/** A stable palette index for an id, so one person keeps one colour everywhere. */
function toneFor(reference: string): string {
  let hash = 0;

  for (let index = 0; index < reference.length; index += 1) {
    hash = (hash * 31 + reference.charCodeAt(index)) | 0;
  }

  return AVATAR_TONES[Math.abs(hash) % AVATAR_TONES.length];
}
