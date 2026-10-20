import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: { sourcemap: false },
  test: {
    environment: 'jsdom', setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    coverage: { provider: 'v8', reporter: ['text', 'lcov'], include: ['src/**/*.{ts,tsx}'], exclude: ['src/**/*.test.*', 'src/test/**', 'src/main.tsx'],
      thresholds: { lines: 80, statements: 80, branches: 70, functions: 70 } }
  }
});
