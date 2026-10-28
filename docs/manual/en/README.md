# Midjourney Proxy Manual (English Entry)

This page is the English entry point of the manual. The chapter bodies are written in Simplified Chinese; this page provides navigation and a short summary only.

## What this manual covers

The manual documents how to deploy, configure, operate and integrate Midjourney Proxy, a service that proxies Discord, YouChuan and the Midjourney official web backend behind one HTTP API and one admin console. It covers installation, configuration, account management, the API surface, the admin console, operations, troubleshooting and architecture.

## Version

- Manual version: **v12.1.6**
- Last updated: **2026-10-28**

## Who it is for

- Developers who need to call the proxy API or build a client on top of it.
- Operations engineers who deploy, upgrade and monitor the service.
- Administrators who manage accounts, tasks and system settings in the console.
- Contributors who need to understand the request flow and task state machine before changing code.

Basic knowledge of Docker or the .NET 8 runtime, reverse proxies and log-based troubleshooting is assumed.

## Quick start summary

1. Start the service with the portable package or the published Docker image, exposing the HTTP port.
2. Open the admin console, sign in, and replace the default administrator credential immediately.
3. Turn off demo mode and open registration/guest access if the instance is private.
4. Add at least one drawing account and wait until it comes online.
5. Submit a minimal drawing request and confirm the task result is returned.

## Manual index

| # | Chapter | Content |
| --- | --- | --- |
| 01 | [Overview](../01-overview.md) | Positioning, three drawing backends, capability matrix, runtime forms, repository layout |
| 02 | [Quick Start](../02-quick-start.md) | Portable package, Docker, first sign-in and security initialization, minimal verification |
| 03 | [Deployment](../03-deployment.md) | Docker Compose, images and ports, volume mounts, reverse proxy, Consul, cluster, online upgrade, Windows IIS |
| 04 | [Configuration](../04-configuration.md) | Configuration sources and precedence, database, Redis, storage, proxy, translation, notification, rate limit, demo mode, registration and guest access |
| 05 | [Account Management](../05-accounts.md) | Three account types, account pool and selection, concurrency and queue, speed mode, vertical domain, working hours, Cloudflare, 2FA, Excel import, sync and reconnect |
| 06 | [API Reference](../06-api-reference.md) | Authentication and path prefix, submit endpoints, task endpoints, task object, callbacks, errors and rate limit, client integration |
| 07 | [Admin Console](../07-admin-console.md) | Sign-in, home page, accounts, users, tasks, drawing test, forbidden words, system settings, log viewer (probe) |
| 08 | [Operations](../08-operations.md) | Logs, health checks, backup and restore, data migration, performance tuning, alerting, upgrade and rollback, hardening |
| 09 | [Troubleshooting](../09-troubleshooting.md) | Accounts offline, Cloudflare, failed or timed-out tasks, queue full, image 404, database and Redis degradation, callback failures, YouChuan and official web errors |
| 10 | [Architecture](../10-architecture.md) | Request flow, task state machine, account selection, Redis queue, distribution and Consul, storage and callbacks |
| 11 | [FAQ and Glossary](../11-faq-glossary.md) | Frequently asked questions, glossary |

## Maintenance and archive

- Maintenance guide: [../MAINTENANCE.md](../MAINTENANCE.md)
- Release archive: [../appendix/releases.md](../appendix/releases.md)
- Commit archive: [../appendix/commits.md](../appendix/commits.md)
- Chinese entry page: [../index.md](../index.md)

## Scope

This manual contains no real credentials; addresses, accounts and keys are placeholders.
