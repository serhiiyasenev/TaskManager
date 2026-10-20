import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['test-results/**', 'playwright-report/**'] },
  ...tseslint.configs.recommended
);
