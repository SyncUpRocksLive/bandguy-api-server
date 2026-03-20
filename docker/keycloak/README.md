Look into:
* disable password logins
* Use Only Google, Apple, Facebook, and/or local "Magic" link email login.
* Enable "Passkeys" (WebAuthn) so you can log in to your dev environment using your laptop's fingerprint scanner?

* Virtual login hosting? auth.syncup.rocks ...

Faster Startup: By running kc.sh build during the Docker build, the server is ready to go immediately. This is critical for environments like Kubernetes where pods need to spin up quickly.

```dockerfile
# Stage 1: Build the optimized Keycloak
FROM quay.io/keycloak/keycloak:latest as builder

# Set build-time options (marked with a tool icon in docs)
ENV KC_DB=postgres
ENV KC_HEALTH_ENABLED=true
ENV KC_METRICS_ENABLED=true

RUN /opt/keycloak/bin/kc.sh build

# Stage 2: Final runtime image
FROM quay.io/keycloak/keycloak:latest
COPY --from=builder /opt/keycloak/ /opt/keycloak/

# Use the --optimized flag to skip the build step at startup
ENTRYPOINT ["/opt/keycloak/bin/kc.sh", "start", "--optimized"]
```