import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { Vacation, ScheduleVacationRequest, RecipientAvailability } from '../models';

@Injectable({ providedIn: 'root' })
export class VacationService {
  private readonly api = inject(ApiService);

  getMyVacations(): Observable<Vacation[]> {
    return this.api.get<Vacation[]>('/v1/vacations');
  }

  schedule(request: ScheduleVacationRequest): Observable<Vacation> {
    return this.api.post<Vacation>('/v1/vacations', request);
  }

  cancel(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/vacations/${id}`);
  }

  checkAvailability(email: string): Observable<RecipientAvailability> {
    return this.api.get<RecipientAvailability>('/v1/vacations/availability', { email });
  }
}
