/** Anything that sits on a stage of a workflow: a participant of a request, a signature of a template. */
export interface StageItem {
  stage: number;
}

/**
 * Renumbers the stages 1..n in order, dropping empty ones, and returns the items sorted by stage.
 * The items themselves are updated in place (their `stage`), so the caller keeps its own object identities.
 */
export function normalizeStages<T extends StageItem>(items: readonly T[]): T[] {
  const stages = [...new Set(items.map(i => i.stage))].sort((a, b) => a - b);
  items.forEach(i => i.stage = stages.indexOf(i.stage) + 1);
  return [...items].sort((a, b) => a.stage - b.stage);
}
