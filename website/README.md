# Documentation site

Astro Starlight site deployed at https://tombiddulph.github.io/PropertyResolvers/.

Use Node.js 24 LTS:

```sh
cd website
npm ci
npm run dev
npm run build
```

Content lives in `src/content/docs`. Production builds include local Pagefind search. The Pages workflow validates pull requests, and deploys only pushes to main or manual runs. Package publication is separate.
