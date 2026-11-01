/** How every output of the Harvey assistant is labelled (R-M12-3): which provider, model and prompt, and when. */
export interface ModelLabel {
  provider: string;
  model: string;
  promptVersion: string;
  generatedAt: string;
}

/** One point of the summary, with the contract's own words it rests on (checked word for word on the server). */
export interface SummaryPoint {
  clause: string;
  point: string;
  quote: string;
}

/** The assistant's summary of one version: clauses a limit kept from being read are listed, never skipped silently. */
export interface ContractSummary {
  version: number;
  points: SummaryPoint[];
  notRead: string[];
  /** Points dropped because their quote was not found in the contract. */
  dropped: number;
  label: ModelLabel;
}

export interface ChangeExplanation {
  change: string;
  previousQuote: string | null;
  currentQuote: string | null;
  explanation: string;
}

/** What changed between two versions, in plain words (R-M12-7). Never a risk rating. */
export interface ExplainChanges {
  from: number;
  to: number;
  changes: ChangeExplanation[];
  unexplained: number;
  label: ModelLabel;
}

export interface AssistantStatus {
  enabled: boolean;
  provider: string;
  model: string;
}

export interface OrganisationSettings {
  modelAvailable: boolean;
  modelEnabled: boolean;
  provider: string;
  model: string;
  alertOffsets: string;
}
