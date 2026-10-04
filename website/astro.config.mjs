import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  site: 'https://tombiddulph.github.io',
  base: '/PropertyResolvers',
  integrations: [starlight({
    title: 'PropertyResolvers',
    description: 'Compile-time structural property access for C#. No reflection. No shared interface required.',
    favicon: '/favicon.svg',
    social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/tombiddulph/PropertyResolvers' }],
    customCss: ['./src/styles/custom.css'],
    sidebar: [
      { label: 'Start here', items: [{ label: 'Quick start', slug: 'getting-started' }] },
      { label: 'Guides', items: [{ autogenerate: { directory: 'guides' } }] },
      { label: 'Reference', items: [{ autogenerate: { directory: 'reference' } }] },
      { label: 'Migration', slug: 'migration' },
    ],
  })],
});
