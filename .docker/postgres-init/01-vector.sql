-- Document indexing migrations require the operator-installed pgvector extension.
-- Integration-test fixtures create isolated databases from template1, so install it there too.
CREATE EXTENSION IF NOT EXISTS vector;
\connect template1
CREATE EXTENSION IF NOT EXISTS vector;
