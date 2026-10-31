import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { AddHolidayRequest, HolidayDto, SaveTeamRequest, TeamDto } from '../models/team.models';

/** Teams that share circuit steps, and the working calendar due dates are counted in (M4). */
@Injectable({ providedIn: 'root' })
export class TeamService {
  private readonly api = inject(ApiService);

  getTeams(): Observable<TeamDto[]> {
    return this.api.get<TeamDto[]>('/v1/teams');
  }

  createTeam(request: SaveTeamRequest): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/teams', request);
  }

  updateTeam(id: string, request: SaveTeamRequest): Observable<void> {
    return this.api.put<void>(`/v1/teams/${id}`, request);
  }

  deleteTeam(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/teams/${id}`);
  }

  getHolidays(): Observable<HolidayDto[]> {
    return this.api.get<HolidayDto[]>('/v1/holidays');
  }

  addHoliday(request: AddHolidayRequest): Observable<{ id: string }> {
    return this.api.post<{ id: string }>('/v1/holidays', request);
  }

  deleteHoliday(id: string): Observable<void> {
    return this.api.delete<void>(`/v1/holidays/${id}`);
  }
}
