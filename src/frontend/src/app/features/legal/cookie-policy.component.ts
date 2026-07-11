import { Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { CookieConsentService } from '../../core/services/cookie-consent.service';

@Component({
  selector: 'sf-cookie-policy',
  standalone: true,
  imports: [RouterLink, MatButtonModule, DatePipe, TranslatePipe],
  template: `
    <div class="legal-page">
      <a class="back-link" routerLink="/dashboard">&larr; {{ 'legal.cookies.backToDashboard' | translate }}</a>
      <h1>{{ 'legal.cookies.title' | translate }}</h1>
      <p class="effective">{{ 'legal.cookies.effective' | translate }}</p>

      <p>{{ 'legal.cookies.intro' | translate }}</p>

      <h2>{{ 'legal.cookies.whatAreTitle' | translate }}</h2>
      <p>{{ 'legal.cookies.whatAreBody' | translate }}</p>

      <h2>{{ 'legal.cookies.cookiesWeUseTitle' | translate }}</h2>

      <table class="cookie-table">
        <thead>
          <tr>
            <th>{{ 'legal.cookies.tableCategory' | translate }}</th>
            <th>{{ 'legal.cookies.tableCookieKey' | translate }}</th>
            <th>{{ 'legal.cookies.tablePurpose' | translate }}</th>
            <th>{{ 'legal.cookies.tableDuration' | translate }}</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><strong>{{ 'legal.cookies.catNecessary' | translate }}</strong></td>
            <td><code>sf_access_token</code>, <code>sf_refresh_token</code></td>
            <td>{{ 'legal.cookies.authPurpose' | translate }}</td>
            <td>{{ 'legal.cookies.authDuration' | translate }}</td>
          </tr>
          <tr>
            <td><strong>{{ 'legal.cookies.catNecessary' | translate }}</strong></td>
            <td><code>sf_cookie_prefs</code></td>
            <td>{{ 'legal.cookies.prefsPurpose' | translate }}</td>
            <td>{{ 'legal.cookies.oneYear' | translate }}</td>
          </tr>
          <tr>
            <td><strong>{{ 'legal.cookies.catFunctional' | translate }}</strong></td>
            <td><code>sf_sidebar_collapsed</code></td>
            <td>{{ 'legal.cookies.sidebarPurpose' | translate }}</td>
            <td>{{ 'legal.cookies.oneYear' | translate }}</td>
          </tr>
          <tr>
            <td><strong>{{ 'legal.cookies.catAnalytics' | translate }}</strong></td>
            <td>{{ 'legal.cookies.notActive' | translate }}</td>
            <td>{{ 'legal.cookies.analyticsPurpose' | translate }}</td>
            <td>&mdash;</td>
          </tr>
        </tbody>
      </table>

      <h2>{{ 'legal.cookies.managingTitle' | translate }}</h2>
      <p>{{ 'legal.cookies.managingBody' | translate }}</p>
      <div class="pref-actions">
        <button mat-flat-button color="primary" (click)="consent.acceptAll()">{{ 'legal.cookies.acceptAll' | translate }}</button>
        <button mat-stroked-button (click)="consent.acceptNecessaryOnly()">{{ 'legal.cookies.necessaryOnly' | translate }}</button>
      </div>

      @if (consent.preferences(); as p) {
        <p class="current-prefs">
          {{ 'legal.cookies.currentPreferences' | translate: { date: (p.decidedAt | date:'mediumDate') } }}
          {{ 'legal.cookies.functionalLabel' | translate }} <strong>{{ (p.functional ? ('legal.cookies.stateOn' | translate) : ('legal.cookies.stateOff' | translate)) }}</strong>,
          {{ 'legal.cookies.analyticsLabel' | translate }} <strong>{{ (p.analytics ? ('legal.cookies.stateOn' | translate) : ('legal.cookies.stateOff' | translate)) }}</strong>.
        </p>
      }

      <h2>{{ 'legal.cookies.thirdPartyTitle' | translate }}</h2>
      <p>{{ 'legal.cookies.thirdPartyBody' | translate }}</p>

      <h2>{{ 'legal.cookies.contactTitle' | translate }}</h2>
      <p>{{ 'legal.cookies.contactBody' | translate }} <a href="mailto:privacy@signflow.app">privacy&#64;signflow.app</a></p>
    </div>
  `,
  styleUrl: './cookie-policy.component.scss'
})
export class CookiePolicyComponent {
  readonly consent = inject(CookieConsentService);
}
