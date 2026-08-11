import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'sf-privacy-policy',
  standalone: true,
  imports: [RouterLink, TranslatePipe],
  template: `
    <div class="legal-page">
      <a class="back-link" routerLink="/dashboard">&larr; {{ 'legal.privacy.backToDashboard' | translate }}</a>
      <h1>{{ 'legal.privacy.title' | translate }}</h1>
      <p class="effective">{{ 'legal.privacy.effective' | translate }}</p>

      <h2>{{ 'legal.privacy.section1Title' | translate }}</h2>
      <p [innerHTML]="'legal.privacy.section1Body' | translate"></p>

      <h2>{{ 'legal.privacy.section2Title' | translate }}</h2>
      <ul>
        <li [innerHTML]="'legal.privacy.section2Item1' | translate"></li>
        <li [innerHTML]="'legal.privacy.section2Item2' | translate"></li>
        <li [innerHTML]="'legal.privacy.section2Item3' | translate"></li>
        <li [innerHTML]="'legal.privacy.section2Item4' | translate"></li>
      </ul>

      <h2>{{ 'legal.privacy.section3Title' | translate }}</h2>
      <ul>
        <li [innerHTML]="'legal.privacy.section3Item1' | translate"></li>
        <li [innerHTML]="'legal.privacy.section3Item2' | translate"></li>
        <li [innerHTML]="'legal.privacy.section3Item3' | translate"></li>
        <li [innerHTML]="'legal.privacy.section3Item4' | translate"></li>
      </ul>

      <h2>{{ 'legal.privacy.section4Title' | translate }}</h2>
      <p [innerHTML]="'legal.privacy.section4Body' | translate"></p>

      <h2>{{ 'legal.privacy.section5Title' | translate }}</h2>
      <ul>
        <li [innerHTML]="'legal.privacy.section5Item1' | translate"></li>
        <li>{{ 'legal.privacy.section5Item2' | translate }}</li>
        <li>{{ 'legal.privacy.section5Item3' | translate }}</li>
      </ul>

      <h2>{{ 'legal.privacy.section6Title' | translate }}</h2>
      <p>{{ 'legal.privacy.section6Body' | translate }}</p>
      <ul>
        <li>{{ 'legal.privacy.section6Item1Before' | translate }} <a routerLink="/privacy/dsar">{{ 'legal.privacy.section6DsarLink' | translate }}</a></li>
        <li>{{ 'legal.privacy.section6Item2Before' | translate }} <a href="mailto:privacy@legaldesk.tn">privacy&#64;legaldesk.tn</a></li>
      </ul>
      <p>{{ 'legal.privacy.section6Footer' | translate }}</p>

      <h2>{{ 'legal.privacy.section7Title' | translate }}</h2>
      <p>{{ 'legal.privacy.section7Body' | translate }}</p>

      <h2>{{ 'legal.privacy.section8Title' | translate }}</h2>
      <p>{{ 'legal.privacy.section8Body' | translate }}</p>

      <h2>{{ 'legal.privacy.section9Title' | translate }}</h2>
      <p>{{ 'legal.privacy.section9BodyBefore' | translate }} <a routerLink="/legal/cookies">{{ 'legal.privacy.section9CookieLink' | translate }}</a> {{ 'legal.privacy.section9BodyAfter' | translate }}</p>

      <h2>{{ 'legal.privacy.section10Title' | translate }}</h2>
      <p>{{ 'legal.privacy.section10Body' | translate }} <a href="mailto:dpo@legaldesk.tn">dpo&#64;legaldesk.tn</a></p>
    </div>
  `,
  styleUrl: './privacy-policy.component.scss'
})
export class PrivacyPolicyComponent {}
