\connect bandguy

CREATE TABLE musician.filesets (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    musician_id             BIGINT NOT NULL REFERENCES app.musicians(id),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted              BOOLEAN NOT NULL DEFAULT FALSE
);
CREATE INDEX idx_filesets_user ON musician.filesets (musician_id);

CREATE TABLE musician.file_versions (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    fileset_id              BIGINT NOT NULL REFERENCES musician.filesets(id),
    version_number          INTEGER NOT NULL,
    file_provider_id        BIGINT NOT NULL REFERENCES app.file_providers(id),
    file_location           TEXT NOT NULL, -- e.g. s3://bucket/key
    file_size_bytes         BIGINT,
    content_type            TEXT, -- e.g. image/jpeg, application/lyric1|2|3|etc
    checksum_sha256         TEXT,
    uploaded_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (fileset_id, version_number)
);
CREATE INDEX idx_file_versions ON musician.file_versions (fileset_id, version_number DESC);

CREATE TABLE musician.songs (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    musician_id             BIGINT NOT NULL REFERENCES app.musicians(id),
    name                    TEXT NOT NULL,
    duration_ms             INTEGER NOT NULL, 
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    in_trash                BOOLEAN NOT NULL DEFAULT FALSE,
    configuration           JSONB NULL -- any additional config (could include tempo/bpm/time signature, key, etc.)
);
CREATE INDEX idx_songs ON musician.songs (musician_id);

CREATE TABLE musician.songs_tracks (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    song_id                 BIGINT NOT NULL REFERENCES musician.songs(id),
    fileset_id              BIGINT NULL REFERENCES musician.filesets(id),
    name                    TEXT NOT NULL, -- e.g. "Lead", "Female Vocals", "Background Vocals", etc.
    type                    TEXT NOT NULL, -- e.g. "Text", "Vocals", "Drums", "Metronome", etc.
    format                  TEXT NOT NULL, -- e.g. LeadVocals, "audio", "tab", "lyric", etc.
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    version_number          INTEGER NULL, -- if null, defaults to latest version in file_versions for the fileset_id
    configuration           JSONB NULL -- any additional config needed for this track (e.g. for a metronome track, could include tempo, time signature, etc.
);
CREATE INDEX idx_songs_tracks ON musician.songs_tracks (song_id);

CREATE TABLE musician.setlists (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    musician_id             BIGINT NOT NULL REFERENCES app.musicians(id),
    name                    TEXT NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (musician_id, name)
);
CREATE INDEX idx_setlists ON musician.setlists (musician_id);

-- Note: We allow the same song to be added to a setlist. Perhaps, want to start the set with an intro - and also end with same intro
CREATE TABLE musician.setlist_songs (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    setlist_id              BIGINT NOT NULL REFERENCES musician.setlists(id),
    song_id                 BIGINT NOT NULL REFERENCES musician.songs(id),
    set_order               INT NOT NULL DEFAULT 0
);
CREATE INDEX idx_setlist_songs ON musician.setlist_songs (setlist_id);
