﻿
import {defineConfig} from 'vite';
import react from '@vitejs/plugin-react';
import {readFileSync} from 'node:fs';

const projectVersion = readFileSync(new URL('../Version.props', import.meta.url), 'utf8')
  .match(/<ProjectVersion>([^<]+)<\/ProjectVersion>/)?.[1];
if (!projectVersion || !/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(projectVersion)) {
  throw new Error('Version.props must contain a canonical Major.Minor.Patch ProjectVersion.');
}
const versionParts = projectVersion.split('.').map(Number);
if (versionParts[0] > 255 || versionParts[1] > 255 || versionParts[2] > 65535) {
  throw new Error('ProjectVersion exceeds MSI limits.');
}

export default defineConfig({
  define: {__PROJECT_VERSION__: JSON.stringify(projectVersion)},
  plugins: [react(), {
    name: 'm2-project-version',
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: 'project-version.json',
        source: JSON.stringify({version: projectVersion})
      });
    },
  }],
  build: {
    outDir: '../.tmp/web-dist/mobile/browser',
    emptyOutDir: true,
  },
});
