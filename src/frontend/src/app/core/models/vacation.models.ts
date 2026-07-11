export interface Vacation {
  id: string;
  startDate: string;
  endDate: string;
  note?: string;
  isActive: boolean;
}

export interface ScheduleVacationRequest {
  startDate: string;
  endDate: string;
  note?: string;
}

export interface RecipientAvailability {
  email: string;
  isOnVacation: boolean;
  availableOn?: string;
}
