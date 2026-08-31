import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterModule } from '@angular/router';

/**
 * Where an address inside the application that matches no route ends up.
 *
 * WHY THIS IS A SCREEN AND NOT A REDIRECT. An unmatched path used to fall through to the wildcard
 * and land the person on the dashboard, or on the sign-in form, with nothing said. Two live menu
 * entries were mis-prefixed for months and behaved exactly like that: click, arrive somewhere
 * else, assume you clicked the wrong thing. Nobody reported it, because nothing looked broken.
 *
 * THE ADDRESS IS SHOWN, verbatim, because it is the one piece of evidence that turns "the menu is
 * odd" into a defect somebody can fix in a minute.
 *
 * IT REUSES THE ACCESS-DENIED STYLES rather than introducing its own. The two are the same shape -
 * a centred card that explains a dead end and offers a way out - and one stylesheet between them
 * is one place for the theme to reach.
 */
@Component({
  selector: 'app-page-not-found',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './page-not-found.html',
  styleUrl: '../access-denied/access-denied.css',
})
export class PageNotFoundComponent {
  private readonly router = inject(Router);

  /** The address that matched nothing. Captured before any navigation replaces it. */
  readonly attemptedUrl = signal(this.router.url);

  goBack(): void {
    void this.router.navigate(['/app/dashboard']);
  }
}
