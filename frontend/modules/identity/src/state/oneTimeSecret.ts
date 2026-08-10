export interface OneTimeSecretState {
  value: string | undefined;
}

export function revealOneTimeSecret(value: string): OneTimeSecretState {
  return { value };
}

export function closeOneTimeSecret(_state: OneTimeSecretState): OneTimeSecretState {
  return { value: undefined };
}
