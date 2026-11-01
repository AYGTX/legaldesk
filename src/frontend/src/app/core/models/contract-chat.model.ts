/** One message of the conversation about a contract. */
export interface ChatTurn {
  role: 'user' | 'assistant';
  content: string;
}

export interface AskContractRequest {
  question: string;
  /** Earlier turns; the server keeps nothing between questions. */
  history: ChatTurn[];
  language: 'en' | 'fr';
}

/** An excerpt the server found word for word in the contract. */
export interface ChatCitation {
  quote: string;
  verified: boolean;
}

export interface AskContractReply {
  answer: string;
  /** Only excerpts that were verified are returned. */
  citations: ChatCitation[];
  /** False when no excerpt checked out: the answer is a lead to verify, not something the contract is shown to say. */
  grounded: boolean;
  language: string;
  label?: import('./assistant.models').ModelLabel;
}
