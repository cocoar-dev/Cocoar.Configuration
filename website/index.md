---
layout: home

hero:
  name: Cocoar.Configuration
  text: Reactive Configuration for .NET
  tagline: Strongly-typed, layered, reactive. Zero ceremony configuration that updates itself.
  image:
    light: /layers.svg
    dark: /layers_dark.svg
    alt: Configuration layering
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: Why Cocoar?
      link: /guide/why-cocoar
    - theme: alt
      text: GitHub
      link: https://github.com/cocoar-dev/Cocoar.Configuration

features:
  - icon: |-
      <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v18"/><path d="M8 7l4-4 4 4"/><path d="M20 21H4"/><path d="M2 15h5l2-6 3 9 2-6h8"/></svg>
    title: Reactive by Default
    details: Subscribe to config changes automatically. No manual IOptionsMonitor wiring. IReactiveConfig&lt;T&gt; updates in real time.
  - icon: |-
      <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><path d="M3 9h18"/><path d="M9 21V9"/></svg>
    title: Zero Ceremony
    details: Define a class, add a rule, inject it. No Configure&lt;T&gt;() calls, no IOptions&lt;T&gt; wrappers. Just your POCO.
  - icon: |-
      <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
    title: Memory-Safe Secrets
    details: Secret&lt;T&gt; with automatic zeroization. Pre-encrypted envelopes decrypted only on Open(). RSA-OAEP + AES-256-GCM.
  - icon: |-
      <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/><line x1="4" y1="22" x2="4" y2="15"/></svg>
    title: Feature Flags & Entitlements
    details: Strongly-typed flags with expiry health, entitlements for plan enforcement. Source-generated descriptors.
  - icon: |-
      <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M2 12h20"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg>
    title: Atomic Updates
    details: All config types update together or not at all. IReactiveConfig&lt;(T1, T2)&gt; guarantees consistent snapshots — no mix of old and new values.
  - icon: |-
      <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"/><path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"/></svg>
    title: Explicit Layering
    details: Rules execute in order, last write wins. File, environment, command-line, HTTP — layer any source with full control.
---
