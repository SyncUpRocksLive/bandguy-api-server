# Stage 1: The Fetcher
FROM registry.access.redhat.com/ubi9-minimal:latest AS fetcher

# No need to install curl, it's already there as curl-minimal
RUN curl -L https://repo1.maven.org/maven2/io/phasetwo/keycloak/keycloak-magic-link/0.57/keycloak-magic-link-0.57.jar \
    -o /tmp/keycloak-magic-link.jar

# Stage 1: Build & Optimize
FROM quay.io/keycloak/keycloak:latest as builder

# Build-time options
ENV KC_DB=postgres
ENV KC_HEALTH_ENABLED=true
ENV KC_METRICS_ENABLED=true
# Copy the fetched JAR from the fetcher stage
COPY --from=fetcher /tmp/keycloak-magic-link.jar /opt/keycloak/providers

# 2. Run the build step to optimize the image
RUN /opt/keycloak/bin/kc.sh build

# Stage 2: Final runtime image
FROM quay.io/keycloak/keycloak:latest
COPY --from=builder /opt/keycloak/ /opt/keycloak/

# Use --optimized to skip the build step at container startup
# Note: Hostname and DB connection info will still come from your Compose env vars
ENTRYPOINT ["/opt/keycloak/bin/kc.sh", "start", "--optimized", "--import-realm"]