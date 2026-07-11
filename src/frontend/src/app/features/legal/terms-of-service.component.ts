import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'sf-terms-of-service',
  standalone: true,
  imports: [RouterLink, TranslatePipe],
  template: `
    <div class="legal-page">
      <a class="back-link" routerLink="/dashboard">&larr; {{ 'legal.terms.backToDashboard' | translate }}</a>
      <h1>{{ 'legal.terms.title' | translate }}</h1>
      <p class="effective">{{ 'legal.terms.effective' | translate }}</p>

      <h2>{{ 'legal.terms.section1Title' | translate }}</h2>
      <p>{{ 'legal.terms.section1Body' | translate }}</p>

      <h2>{{ 'legal.terms.section2Title' | translate }}</h2>
      <p>{{ 'legal.terms.section2Body' | translate }}</p>

      <h2>{{ 'legal.terms.section3Title' | translate }}</h2>
      <p [innerHTML]="'legal.terms.section3Body' | translate"></p>

      <h2>{{ 'legal.terms.section4Title' | translate }}</h2>
      <ul>
        <li>{{ 'legal.terms.section4Item1' | translate }}</li>
        <li>{{ 'legal.terms.section4Item2' | translate }}</li>
        <li>{{ 'legal.terms.section4Item3' | translate }}</li>
        <li>{{ 'legal.terms.section4Item4' | translate }}</li>
      </ul>

      <h2>{{ 'legal.terms.section5Title' | translate }}</h2>
      <p>{{ 'legal.terms.section5BodyBefore' | translate }} <a routerLink="/legal/privacy">{{ 'legal.terms.section5PrivacyLink' | translate }}</a>.</p>

      <h2>{{ 'legal.terms.section6Title' | translate }}</h2>
      <p>{{ 'legal.terms.section6Body' | translate }}</p>

      <h2>{{ 'legal.terms.section7Title' | translate }}</h2>
      <p>{{ 'legal.terms.section7Body' | translate }}</p>

      <h2>{{ 'legal.terms.section8Title' | translate }}</h2>
      <p>{{ 'legal.terms.section8Body' | translate }}</p>

      <h2>{{ 'legal.terms.section9Title' | translate }}</h2>
      <p>{{ 'legal.terms.section9Body' | translate }}</p>

      <h2>{{ 'legal.terms.section10Title' | translate }}</h2>
      <p>{{ 'legal.terms.section10Body' | translate }}</p>

      <h2>{{ 'legal.terms.section11Title' | translate }}</h2>
      <p>{{ 'legal.terms.section11Body' | translate }} <a href="mailto:legal@signflow.app">legal&#64;signflow.app</a></p>
    </div>
  `,
  styleUrl: './terms-of-service.component.scss'
})
export class TermsOfServiceComponent {}
