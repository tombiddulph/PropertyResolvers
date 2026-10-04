import { readdir, readFile, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = new URL('../dist/', import.meta.url);
const files = [];
async function walk(path) {
  for (const entry of await readdir(path, { withFileTypes: true })) {
    const child = join(path, entry.name);
    if (entry.isDirectory()) await walk(child);
    else if (entry.name.endsWith('.html')) files.push(child);
  }
}
await walk(fileURLToPath(root));
const failures = [];
for (const file of files) {
  const html = await readFile(file, 'utf8');
  for (const [, href] of html.matchAll(/(?:href|src)="(\/[^"#?]*)(?:[#?][^"]*)?"/g)) {
    if (!href.startsWith('/PropertyResolvers/')) {
      failures.push(`${file}: missing project base in ${href}`);
      continue;
    }
    const relative = decodeURIComponent(href.slice('/PropertyResolvers/'.length));
    const target = new URL(relative || './', root);
    try {
      const info = await stat(target);
      if (info.isDirectory()) await stat(new URL('index.html', target.href.endsWith('/') ? target : new URL(target.href + '/')));
    } catch { failures.push(`${file}: missing target ${href}`); }
  }
}
if (failures.length) throw new Error(failures.join('\n'));
console.log(`Validated internal links and assets in ${files.length} HTML pages.`);
