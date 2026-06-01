import { defineConfig } from 'vitepress'
import llmstxt from 'vitepress-plugin-llms'

export default defineConfig({
  title: 'Cocoar.Configuration',
  description: 'Reactive, strongly-typed configuration for .NET',

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo_light.svg' }],
    ['link', { rel: 'alternate', type: 'text/plain', href: '/llms.txt', title: 'LLM documentation (summary)' }],
    ['link', { rel: 'alternate', type: 'text/plain', href: '/llms-full.txt', title: 'LLM documentation (full)' }],
  ],

  vite: {
    plugins: [llmstxt({
      excludeUnnecessaryFiles: false,
      ignoreFiles: ['changelog.md'],
    })],
  },

  themeConfig: {
    logo: {
      light: '/logo_light.svg',
      dark: '/logo_dark.svg',
    },

    siteTitle: 'Cocoar.Configuration v5',

    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'Reference', link: '/reference/packages' },
      { text: 'ADR', link: '/adr/' },
      { text: 'Roadmap', link: '/roadmap/overview' },
      { text: 'Changelog', link: '/changelog' },
      { text: 'LLM Docs', link: '/llms-full.txt', target: '_blank' },
      { text: 'NuGet', link: 'https://www.nuget.org/packages/Cocoar.Configuration' },
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Introduction',
          items: [
            { text: 'Getting Started', link: '/guide/getting-started' },
            { text: 'Why Cocoar?', link: '/guide/why-cocoar' },
            { text: 'Working with Certificates', link: '/guide/certificates' },
          ],
        },
        {
          text: 'Configuration',
          items: [
            { text: 'Rules & Layering', link: '/guide/configuration/rules' },
            { text: 'Required vs Optional', link: '/guide/configuration/required-optional' },
            { text: 'Setup & Type Exposure', link: '/guide/configuration/setup' },
            { text: 'Config-Aware Rules <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/configuration/config-aware' },
            { text: 'Conditional Rules <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/configuration/conditional-rules' },
            { text: 'Aggregate Rules <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/configuration/aggregate-rules' },
          ],
        },
        {
          text: 'Providers',
          items: [
            { text: 'Overview', link: '/guide/providers/overview' },
            { text: 'File', link: '/guide/providers/file' },
            { text: 'Environment Variables', link: '/guide/providers/environment' },
            { text: 'Command Line', link: '/guide/providers/command-line' },
            { text: 'HTTP Polling', link: '/guide/providers/http-polling' },
            { text: 'Microsoft IConfiguration', link: '/guide/providers/microsoft-adapter' },
            { text: 'Static & Observable', link: '/guide/providers/static-observable' },
            { text: 'Writable Store', link: '/guide/providers/writable-store' },
            { text: 'Custom Providers <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/providers/custom' },
          ],
        },
        {
          text: 'Dependency Injection',
          items: [
            { text: 'DI Setup', link: '/guide/di/setup' },
            { text: 'ASP.NET Core', link: '/guide/di/aspnetcore' },
            { text: 'Lifetimes & Registration <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/di/lifetimes' },
            { text: 'Service-Backed Config <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/di/service-backed' },
          ],
        },
        {
          text: 'Reactive Updates',
          items: [
            { text: 'IReactiveConfig<T>', link: '/guide/reactive/basics' },
            { text: 'Reactive Tuples <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/reactive/tuples' },
            { text: 'Debouncing', link: '/guide/reactive/debouncing' },
          ],
        },
        {
          text: 'Feature Flags & Entitlements',
          items: [
            { text: 'Concepts', link: '/guide/flags/concepts' },
            { text: 'Defining Flags', link: '/guide/flags/defining-flags' },
            { text: 'Defining Entitlements', link: '/guide/flags/defining-entitlements' },
            { text: 'Registration', link: '/guide/flags/registration' },
            { text: 'Context Resolvers <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/flags/context-resolvers' },
            { text: 'REST Endpoints <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/flags/rest-endpoints' },
            { text: 'Expiry & Health', link: '/guide/flags/expiry-health' },
          ],
        },
        {
          text: 'Multi-Tenancy',
          items: [
            { text: 'Overview <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/multi-tenancy/overview' },
          ],
        },
        {
          text: 'Secrets',
          items: [
            { text: 'Overview', link: '/guide/secrets/overview' },
            { text: 'Secret<T> & Leases', link: '/guide/secrets/secret-type' },
            { text: 'Encryption Setup', link: '/guide/secrets/encryption-setup' },
            { text: 'Publishing Encryption Keys <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/secrets/key-publishing' },
            { text: 'Browser & Client Encryption <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/secrets/client-encryption' },
            { text: 'CLI Tools', link: '/guide/secrets/cli' },
            { text: 'Certificate Caching <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/secrets/certificate-caching' },
            { text: 'Security Model <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/secrets/security-model' },
          ],
        },
        {
          text: 'Health Monitoring',
          items: [
            { text: 'Overview', link: '/guide/health/overview' },
            { text: 'ASP.NET Core Health Checks', link: '/guide/health/aspnetcore' },
            { text: 'Logging & Diagnostics <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/health/logging' },
            { text: 'Performance <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/health/performance' },
          ],
        },
        {
          text: 'Testing',
          items: [
            { text: 'Test Overrides', link: '/guide/testing/overrides' },
            { text: 'Integration Testing', link: '/guide/testing/integration' },
            { text: 'Testing Strategy <span class="badge-adv" title="Advanced topic"></span>', link: '/guide/testing/strategy' },
          ],
        },
        {
          text: 'Analyzers',
          items: [
            { text: 'Overview', link: '/guide/analyzers/overview' },
            { text: 'Configuration Diagnostics', link: '/guide/analyzers/configuration' },
            { text: 'Flags Diagnostics', link: '/guide/analyzers/flags' },
          ],
        },
        {
          text: 'How-To',
          items: [
            { text: 'From IOptions', link: '/guide/how-to/from-ioptions' },
          ],
        },
        {
          text: 'Migration',
          collapsed: true,
          items: [
            { text: 'v4 → v5', link: '/guide/migration/v4-to-v5' },
            { text: 'v3 → v4', link: '/guide/migration/v3-to-v4' },
            { text: 'v2 → v3', link: '/guide/migration/v2-to-v3' },
          ],
        },
      ],
      '/reference/': [
        {
          text: 'Reference',
          items: [
            { text: 'Package Overview', link: '/reference/packages' },
            { text: 'Health API', link: '/reference/health-api' },
            { text: 'CLI Commands', link: '/reference/cli-commands' },
            { text: 'Analyzer Diagnostics', link: '/reference/analyzer-diagnostics' },
            { text: 'Examples', link: '/reference/examples' },
          ],
        },
      ],
      '/roadmap/': [
        {
          text: 'Roadmap',
          items: [
            { text: 'Overview', link: '/roadmap/overview' },
            { text: 'ConfigHub', link: '/roadmap/confighub' },
            { text: 'Cloud Providers', link: '/roadmap/cloud-providers' },
            { text: 'Database Provider', link: '/roadmap/database-provider' },
          ],
        },
      ],
      '/adr/': [
        {
          text: 'Architecture Decision Records',
          items: [
            { text: 'Overview', link: '/adr/' },
            { text: 'ADR-001 · Capabilities System', link: '/adr/ADR-001-capabilities-system' },
            { text: 'ADR-002 · Atomic Reactive Updates', link: '/adr/ADR-002-atomic-reactive-updates' },
            { text: 'ADR-003 · Provider Consistency', link: '/adr/ADR-003-provider-consistency-empty-objects' },
            { text: 'ADR-004 · Aggregate Rules', link: '/adr/ADR-004-aggregate-rules' },
            { text: 'ADR-005 · Multi-Tenant Configuration', link: '/adr/ADR-005-multi-tenant-configuration' },
            { text: 'ADR-006 · DI-aware Configuration', link: '/adr/ADR-006-di-aware-configuration' },
          ],
        },
      ],
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/cocoar-dev/Cocoar.Configuration' },
    ],

    search: {
      provider: 'local',
    },

    footer: {
      message: 'Released under the Apache-2.0 License.',
      copyright: 'Copyright 2025-present Cocoar',
    },
  },
})
