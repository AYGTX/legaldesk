import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'sf-not-found',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="not-found">
      <mat-icon class="icon">search_off</mat-icon>
      <h1>404</h1>
      <p>{{ 'shared.notFound.message' | translate }}</p>
      <button mat-flat-button routerLink="/dashboard">{{ 'shared.notFound.backHome' | translate }}</button>
    </div>
  `,
  styleUrl: './not-found.component.scss'
})
export class NotFoundComponent {}
