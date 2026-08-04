import { defineConfig } from 'vitest/config';

// The client is browser code: it touches the DOM, fetch, localStorage and object URLs. jsdom gives it
// somewhere to run so the logic can be tested without driving a real browser by hand — which is how
// everything here was verified before this harness existed.
export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['test/**/*.test.ts'],
    // The generated client is 19k lines of NSwag output; nothing here tests it, and it should never
    // count towards this project's coverage story.
    coverage: { exclude: ['src/generated/**'] },
  },
});
