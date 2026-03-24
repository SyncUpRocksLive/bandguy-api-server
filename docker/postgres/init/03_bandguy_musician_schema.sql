\connect bandguy

CREATE TABLE musician.file_sets (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    musician_id             UUID NOT NULL REFERENCES app.musicians(id),
    file_name               TEXT NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted              BOOLEAN NOT NULL DEFAULT FALSE,

    UNIQUE (musician_id, file_name)
);
CREATE INDEX idx_file_sets_user ON musician.file_sets (musician_id);

CREATE TABLE musician.file_versions (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    file_set_id             BIGINT NOT NULL REFERENCES musician.file_sets(id),
    version_number          INTEGER NOT NULL,
    file_provider_id        BIGINT NOT NULL REFERENCES app.file_providers(id),
    file_location           TEXT NOT NULL, -- e.g. s3://bucket/key
    file_size_bytes         BIGINT,
    content_type            TEXT, -- e.g. image/jpeg, application/lyric1|2|3|etc
    checksum_sha256         TEXT,
    uploaded_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (file_set_id, version_number)
);
CREATE INDEX idx_file_versions ON musician.file_versions (file_set_id, version_number DESC);

CREATE TABLE musician.songs (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    musician_id             UUID NOT NULL REFERENCES app.musicians(id),
    name                    TEXT NOT NULL,
    duration_ms             INTEGER NOT NULL, 
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    configuration           JSONB NULL, -- any additional config (could include tempo/bpm/time signature, key, etc.)

    UNIQUE (musician_id, name)
);
CREATE INDEX idx_songs ON musician.songs (musician_id);

CREATE TABLE musician.songs_tracks (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    song_id                 BIGINT NOT NULL REFERENCES musician.songs(id),
    file_set_id             BIGINT NULL REFERENCES musician.file_sets(id),
    name                    TEXT NOT NULL, -- e.g. "Lead", "Female Vocals", "Background Vocals", etc.
    type                    TEXT NOT NULL, -- e.g. "Text", "Vocals", "Drums", "Metronome", etc.
    format                  TEXT NOT NULL, -- e.g. LeadVocals, "audio", "tab", "lyric", etc.
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    version_number          INTEGER NULL, -- if null, defaults to latest version in file_versions for the file_set_id
    configuration           JSONB NULL -- any additional config needed for this track (e.g. for a metronome track, could include tempo, time signature, etc.
);
CREATE INDEX idx_songs_tracks ON musician.songs_tracks (song_id);

CREATE TABLE musician.set_lists (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    musician_id             UUID NOT NULL REFERENCES app.musicians(id),
    setlist_name            TEXT NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (musician_id, setlist_name)
);
CREATE INDEX idx_set_list ON musician.set_lists (musician_id);

CREATE TABLE musician.set_list_songs (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    setlist_id              BIGINT NOT NULL REFERENCES musician.set_lists(id),
    song_id                 BIGINT NOT NULL REFERENCES musician.songs(id),

    UNIQUE (setlist_id, song_id)
);
CREATE INDEX idx_set_list_songs ON musician.set_list_songs (setlist_id);
