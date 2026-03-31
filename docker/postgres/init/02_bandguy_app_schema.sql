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
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    identity_provider       TEXT NOT NULL,
    external_uuid           UUID NOT NULL,
    username                TEXT NOT NULL,
    email                   TEXT NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_login              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_disabled             BOOLEAN NOT NULL DEFAULT FALSE,
    disabled_reason         TEXT NULL,

    CONSTRAINT uq_musician_identity_uuid UNIQUE (identity_provider, external_uuid)
);
-- TODO: Clean this up - we likely want to allow multiple musicians to have the same email (e.g. if they sign up with different providers), but for now we'll just require unique emails. We can always relax this later if needed.
-- CREATE UNIQUE INDEX uq_musicians_email ON app.musicians (LOWER(email));

CREATE TABLE app.file_providers (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name                    TEXT NOT NULL,
    type                    TEXT NOT NULL,
    configuration           JSONB NOT NULL
);
CREATE UNIQUE INDEX uq_file_providers ON app.file_providers (LOWER(type), LOWER(name));
