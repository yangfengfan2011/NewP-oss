// Compatibility entrypoint. Bootstrap/auth/home callers use /core, while CRUD hosts use /crud.
export * from './core';
export * from './crud';
