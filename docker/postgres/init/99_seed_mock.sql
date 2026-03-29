\connect bandguy

-- Insert initial schema version (used for tracking database migrations)
INSERT INTO app.schema_versions (version) VALUES (1);

-- Create mock users

-- Create file providers
-- TODO: Put Encrypted token/secret here
INSERT INTO app.file_providers (name, type, configuration) 
VALUES (
    'data-store', 
    's3', 
    '{ 
        "Buckets": {"song": "data"}, 
        "Region": "us-east-1", 
        "ServiceURL": "http://127.0.0.1:9090", 
        "ForcePathStyle": true, 
        "AccessKey": "test", 
        "Secret": "test" 
    }'
);