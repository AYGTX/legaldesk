export interface Vacation {
  id: string;
  startDate: string;
  endDate: string;
  note?: string;
  isActive: boolean;
  /** The colleague whose steps open while this person is away (R-M1-8). */
  delegateUserId?: string | null;
  delegateName?: string | null;
}

export interface ScheduleVacationRequest {
  startDate: string;
  endDate: string;
  note?: string;
  delegateUserId?: string | null;
}

export interface RecipientAvailability {
  email: string;
  isOnVacation: boolean;
  availableOn?: string;
}
