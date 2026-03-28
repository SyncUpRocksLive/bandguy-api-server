This repo contains a fully working local devlopment environment for bandguy.

It assumes that the SPA (NPM) react app is running on Port 9000 of localhost. And, the .Net Web API is running on port 9001.

Additionally, need to have a hosts file entry of syncup.local for 127.0.0.1

After starting docker, you would access all of these resources via http://syncup.local:7080

## Traefik - Proxy/Routing

Behind the scenes, Traefik is routing/proxying data to actual bandend services (KeyCloak - for auth/login, API for all /api routes, and the SPA for any other query path). Traefik keeps CORs issues away by hiding different ports, and acts as a transparent proxy and URL rewriter - allowing shared cookies across services in the same domain.

## s3proxy

Basic S3 layer to simulate connecting to an S3 bucket (file store). Since not using AWS, not looking to spin up localstack here

## KeyCloak

Keycloak is an open-source Identity and Access Management (IAM) solution that centralizes user authentication and authorization so your applications don't have to manage login forms or store passwords. It provides a secure "handshake" via standard protocols like OpenID Connect, allowing you to implement features like Single Sign-On (SSO), social logins, and multi-factor authentication across your entire ecosystem with minimal custom code.

**Eventually**, will want to style up and customize the login process.