/** A group of people who share a step of a circuit: any one member acts for the team (R-M4-3). */
export interface TeamDto {
  id: string;
  name: string;
  description?: string | null;
  members: TeamMemberDto[];
}

export interface TeamMemberDto {
  userId: string;
  name: string;
  email: string;
  isActive: boolean;
}

export interface SaveTeamRequest {
  name: string;
  description?: string | null;
  memberIds: string[];
}

/** A day off in the organisation's working calendar; due dates skip it (R-M4-10). */
export interface HolidayDto {
  id: string;
  /** yyyy-MM-dd */
  date: string;
  name: string;
  /** The same day every year (Independence Day), rather than one date (a religious holiday this year). */
  everyYear: boolean;
}

export interface AddHolidayRequest {
  date: string;
  name: string;
  everyYear: boolean;
}
