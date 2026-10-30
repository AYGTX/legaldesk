import { Injectable, inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { TranslateService } from '@ngx-translate/core';
import { Observable, filter, interval, map, merge, of, switchMap, take, throwError, timeout, catchError } from 'rxjs';
import { Review } from '../models';
import { DocumentService } from './document.service';
import { HubService } from './hub.service';
import { ConfirmService } from './confirm.service';

/** A review or a send the API refused, already worded for the reader. */
export class ReviewError extends Error {
  constructor(message: string, readonly code: string | null) {
    super(message);
  }
}

/**
 * Harvey's review as the pages use it (rework spec M8): start a run, wait for its result (pushed over SignalR, with a
 * slow poll in case the connection is down), and send a contract through the review gate, asking for a reason when
 * high-risk findings are still open.
 */
@Injectable({ providedIn: 'root' })
export class ReviewService {
  private readonly documents = inject(DocumentService);
  private readonly hub = inject(HubService);
  private readonly confirm = inject(ConfirmService);
  private readonly translate = inject(TranslateService);

  private readonly pushes$ = toObservable(this.hub.lastReviewProgress);

  /** A little over the server's 120 s limit, so the server's own "timed out" is what the reader sees. */
  private static readonly WaitMs = 150_000;

  /** Starts a review and emits the review once its run has finished; a failed run errors with a worded ReviewError. */
  run(documentId: string): Observable<Review> {
    return this.documents.startReview(documentId).pipe(
      catchError(err => throwError(() => this.toError(err))),
      switchMap(run => this.waitFor(documentId, run.id))
    );
  }

  /** Emits the review once run `runId` has finished. */
  waitFor(documentId: string, runId: string): Observable<Review> {
    const pushed$ = this.pushes$.pipe(
      filter(e => !!e && e.documentId === documentId && e.runId === runId && (e.status === 'Completed' || e.status === 'Failed')));
    return merge(of(0), pushed$, interval(3000)).pipe(
      switchMap(() => this.documents.getReview(documentId)),
      filter(r => r.latestRun?.id === runId && (r.latestRun.status === 'Completed' || r.latestRun.status === 'Failed')),
      take(1),
      timeout(ReviewService.WaitMs),
      catchError(err => throwError(() => err instanceof ReviewError ? err : this.toError(err, 'review.timeout'))),
      map(r => {
        if (r.latestRun!.status === 'Failed') throw this.errorFor(r.latestRun!.errorCode ?? 'review.unavailable');
        return r;
      })
    );
  }

  /**
   * Sends the contract through the review gate (R-M8-8). With open high-risk findings the API asks for a reason: the
   * reader is asked for one and the send is retried with it. Emits true when sent, false when the reader cancelled.
   */
  send(documentId: string, reason?: string): Observable<boolean> {
    return this.documents.send(documentId, reason).pipe(
      map(() => true),
      catchError(err => {
        const errors = err.error?.errors as Record<string, string[]> | undefined;
        if (errors?.['readinessWarning'] && reason === undefined) {
          return this.confirm.prompt({
            title: this.translate.instant('shared.review.send.reasonTitle'),
            message: this.translate.instant('shared.review.send.reasonMessage'),
            inputLabel: this.translate.instant('shared.review.send.reasonLabel'),
            inputType: 'textarea',
            confirmLabel: this.translate.instant('shared.review.send.reasonConfirm')
          }).pipe(switchMap(given => given?.trim() ? this.send(documentId, given.trim()) : of(false)));
        }
        if (errors?.['readiness']) return throwError(() => new ReviewError(this.translate.instant('shared.review.send.blocked'), 'readiness'));
        if (errors?.['readinessRequired']) return throwError(() => new ReviewError(this.translate.instant('shared.review.send.required'), 'readinessRequired'));
        return throwError(() => this.toError(err));
      })
    );
  }

  /** The reader's wording for an API error: by its code when it has one, else the server's message. */
  toError(err: unknown, fallbackCode: string | null = null): ReviewError {
    const e = err as { error?: { code?: string; detail?: string }; name?: string };
    const code = e?.error?.code ?? (e?.name === 'TimeoutError' ? 'review.timeout' : fallbackCode);
    if (code) return this.errorFor(code);
    return new ReviewError(e?.error?.detail ?? this.translate.instant('review.failed'), null);
  }

  private errorFor(code: string): ReviewError {
    const key = `shared.review.errors.${code}`;
    const text = this.translate.instant(key);
    return new ReviewError(text === key ? this.translate.instant('review.failed') : text, code);
  }
}
