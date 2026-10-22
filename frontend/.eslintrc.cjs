module.exports = {
  root: true,
  ignorePatterns: ['**/dist/**', '**/node_modules/**', '**/coverage/**'],
  overrides: [
    {
      files: ['**/*.ts', '**/*.tsx'],
      env: {
        browser: true,
        es2022: true,
        node: true,
      },
      parser: '@typescript-eslint/parser',
      parserOptions: {
        ecmaVersion: 'latest',
        sourceType: 'module',
        ecmaFeatures: {
          jsx: true,
        },
      },
      plugins: ['@typescript-eslint', 'react-hooks', 'react-refresh'],
      extends: [
        'eslint:recommended',
        'plugin:@typescript-eslint/recommended',
        'plugin:react-hooks/recommended',
      ],
      rules: {
        '@typescript-eslint/no-unused-vars': ['error', {
          argsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
          destructuredArrayIgnorePattern: '^_',
          varsIgnorePattern: '^_',
        }],
      },
    },
    {
      files: ['packages/shell/src/**/*.{ts,tsx}'],
      rules: {
        'react-refresh/only-export-components': ['error', { allowConstantExport: true }],
      },
    },
    {
      files: [
        'packages/shell/src/contexts/**/*.{ts,tsx}',
        'packages/shell/src/metadata/runtimeMetadataContext.tsx',
      ],
      rules: {
        'react-refresh/only-export-components': 'off',
      },
    },
  ],
};
