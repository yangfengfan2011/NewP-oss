// Compatibility entrypoint. Bootstrap/auth/home callers use /core, while CRUD hosts use /crud.
export * from './core';
export * from './crud';
export { default as DataScopeSubjectEditor } from './DataScopeSubjectEditor';
export type { DataScopeSubject, SubjectType } from './DataScopeSubjectEditor';
export { default as OpinionCheckbox } from './OpinionCheckbox';
export { default as DataScopeSubjectTable } from './DataScopeSubjectTable';
export { orderDataScopeSubjects, rankDataScopeSubjects } from './dataScopeSubjectOrder';
export { default as ObjectAuthorizationInput } from './ObjectAuthorizationInput';
export type { ObjectAuthorizationTarget } from './ObjectAuthorizationInput';
export { createObjectAuthorizationActions } from './objectAuthorizationActions';
export { default as ObjectAuthorizationPage } from './ObjectAuthorizationPage';
export { createObjectOwnershipActions } from './objectOwnershipActions';
export type { ObjectOwnershipInitializationTarget } from './ObjectOwnershipInitializationInput';
