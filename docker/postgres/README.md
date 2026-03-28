## Database Design

### Databases

**bandguy** - Primary database maintaining media, band, user/band relations, setlists, etc

**keycloak** - Holds OAuth/OIDC/SAML/etc providers, signin with Google, Realms, emails, configuration.

**webapi** - Any items specific to the Web API Backend. Cookie/Encryption keys. Audit records