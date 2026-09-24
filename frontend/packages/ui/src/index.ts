// Compatibility entrypoint. Bootstrap/auth/home callers use /core, while CRUD hosts use /crud.
export * from './core';
export * from './crud';
export { default as DataScopeSubjectEditor } from './DataScopeSubjectEditor';
export type { DataScopeSubject, SubjectType } from './DataScopeSubjectEditor';
export { default as OpinionCheckbox } from './OpinionCheckbox';
