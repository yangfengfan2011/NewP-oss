/**
 * Creates a zero-argument capability loader whose retry uses a separately declared import.
 * The runtime owns single-flight and successful-result caching; this helper only remembers
 * that the primary import rejected so a later invocation can use a different ESM URL.
 */
export function createRecoverableCapabilityLoader<TModule>(
  primary: () => Promise<TModule>,
  recovery: () => Promise<TModule>,
): () => Promise<TModule> {
  let primaryFailed = false;

  return async () => {
    if (primaryFailed) {
      return recovery();
    }

    try {
      return await primary();
    } catch (error) {
      primaryFailed = true;
      throw error;
    }
  };
}
