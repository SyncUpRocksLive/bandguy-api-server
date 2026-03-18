\connect bandguy

CREATE EXTENSION IF NOT EXISTS "pgcrypto"; -- for UUID generation

-- Create a new schema
CREATE SCHEMA IF NOT EXISTS app;
CREATE SCHEMA IF NOT EXISTS musician;

CREATE TABLE app.schema_versions (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    version                 BIGINT NOT NULL,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE app.musicians (
    id                      UUID PRIMARY KEY DEFAULT uuidv7(),
    display_name            TEXT NOT NULL,
    email                   TEXT NOT NULL,
    email_verified          BOOLEAN NOT NULL DEFAULT FALSE,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_disabled             BOOLEAN NOT NULL DEFAULT FALSE,
    disabled_reason         TEXT NULL
);
CREATE UNIQUE INDEX uq_musicians_email ON app.musicians (LOWER(email));

CREATE TABLE app.external_auth_providers (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    musician_id             UUID NOT NULL REFERENCES app.musicians(id),
    provider_name           TEXT NOT NULL, -- e.g. 'google', 'facebook', 'github', 'microsoft'
    provider_user_id        TEXT NOT NULL, -- the unique user ID returned by the provider
    provider_email          TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (provider_name, provider_user_id)
);
CREATE INDEX idx_external_auth_providers_musician_id ON app.external_auth_providers (musician_id);

CREATE TABLE app.file_providers (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    display_name            TEXT NOT NULL,
    type                    TEXT NOT NULL,
    config                  JSONB NOT NULL
);
CREATE UNIQUE INDEX uq_file_providers ON app.file_providers (LOWER(type), LOWER(display_name));
