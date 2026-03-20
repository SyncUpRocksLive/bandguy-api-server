\connect webapi

-- Create a new schema
CREATE SCHEMA IF NOT EXISTS app;

CREATE TABLE app.data_protection (
    id bigint       GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    friendly_name   TEXT NOT NULL,
    xml_data        TEXT NOT NULL,
    created_at      TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE app.schema_versions (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    version                 BIGINT NOT NULL,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

