**Purpose** This repo contains a fully working local devlopment environment for bandguy.

**Important** - Running a complete app locally involves having the main frontend/SPAs (NPM) running on Port 9000 of localhost. And, the .Net/C# Web API is running on port 9001. And docker compose stack up and running.

**Additionally**, need to have a hosts file entry of syncup.local for 127.0.0.1 (needed for keycloak auth logins and traefik routing)

After starting docker - "docker compose up", API, and SPAs, you would access all of these resources via http://syncup.local:7080

## Traefik - Proxy/Routing

Behind the scenes, Traefik is routing/proxying data to actual bandend services (KeyCloak - for auth/login, API for all /api routes, and the SPA for any other query path). Traefik keeps CORs issues away by hiding different ports, and acts as a transparent proxy and URL rewriter - allowing shared cookies across services in the same domain.

## s3proxy

Basic S3 layer to simulate connecting to an S3 bucket (file store). Used for file storage blobs of song tracks.

## KeyCloak

Keycloak is an open-source Identity and Access Management (IAM) solution that centralizes user authentication and authorization so your applications don't have to manage login forms or store passwords. It provides a secure "handshake" via standard protocols like OpenID Connect, allowing you to implement features like Single Sign-On (SSO), social logins, and multi-factor authentication across your entire ecosystem with minimal custom code.

## Postgres

Postgresql is the main datastorage. All song metadata stored here, and files stored in S3. The initial schema is stored under postgres\init. If needing to recreate, run "docker compose down -v" to destroy current database on disk.

## Mailhog

Used as mock SFTP server for KeyCloak, app usages. Presents a simple webapi where you can see all mails sent.