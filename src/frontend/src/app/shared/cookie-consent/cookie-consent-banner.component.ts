import { Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { CookieConsentService } from '../../core/services/cookie-consent.service';

@Component({
  selector: 'sf-cookie-consent-banner',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatIconModule, TranslatePipe],
  template: `
    @if (consent.bannerVisible()) {
      <div class="banner-backdrop">
        <div class="banner" role="dialog" [attr.aria-label]="'shared.cookieConsent.ariaLabel' | translate">
          <div class="banner-header">
            <mat-icon class="cookie-icon">cookie</mat-icon>
            <h2>{{ 'shared.cookieConsent.title' | translate }}</h2>
          </div>

          <p class="banner-desc">
            {{ 'shared.cookieConsent.description' | translate }}
            <a routerLink="/legal/cookies">{{ 'shared.cookieConsent.cookiePolicy' | translate }}</a> ·
            <a routerLink="/legal/privacy">{{ 'shared.cookieConsent.privacyPolicy' | translate }}</a>
          </p>

          @if (showCustomise()) {
            <div class="custom-options">
              <label class="opt-row">
                <div>
                  <strong>{{ 'shared.cookieConsent.necessary' | translate }}</strong>
                  <span>{{ 'shared.cookieConsent.necessaryDesc' | translate }}</span>
                </div>
                <mat-checkbox [checked]="true" [disabled]="true"></mat-checkbox>
              </label>
              <label class="opt-row">
                <div>
                  <strong>{{ 'shared.cookieConsent.functional' | translate }}</strong>
                  <span>{{ 'shared.cookieConsent.functionalDesc' | translate }}</span>
                </div>
                <mat-checkbox [(ngModel)]="functionalEnabled" [ngModelOptions]="{standalone:true}"></mat-checkbox>
              </label>
              <label class="opt-row">
                <div>
                  <strong>{{ 'shared.cookieConsent.analytics' | translate }}</strong>
                  <span>{{ 'shared.cookieConsent.analyticsDesc' | translate }}</span>
                </div>
                <mat-checkbox [(ngModel)]="analyticsEnabled" [ngModelOptions]="{standalone:true}"></mat-checkbox>
              </label>
            </div>
          }

          <div class="banner-actions">
            @if (!showCustomise()) {
              <button mat-stroked-button (click)="showCustomise.set(true)">{{ 'shared.cookieConsent.customise' | translate }}</button>
            } @else {
              <button mat-stroked-button (click)="saveCustom()">{{ 'shared.cookieConsent.savePreferences' | translate }}</button>
            }
            <button mat-stroked-button (click)="consent.acceptNecessaryOnly()">{{ 'shared.cookieConsent.necessaryOnly' | translate }}</button>
            <button mat-flat-button color="primary" (click)="consent.acceptAll()">{{ 'shared.cookieConsent.acceptAll' | translate }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styleUrl: './cookie-consent-banner.component.scss'
})
export class CookieConsentBannerComponent {
  readonly consent = inject(CookieConsentService);
  readonly showCustomise = signal(false);
  functionalEnabled = true;
  analyticsEnabled = false;

  saveCustom(): void {
    this.consent.saveCustom(this.functionalEnabled, this.analyticsEnabled);
  }
}
