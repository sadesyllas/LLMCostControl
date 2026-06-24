-- Postgres initialization script (runs once on first container start).
-- Creates the application database and user used by both the Tracker API and
-- the Admin App in the local development environment.

SELECT 'CREATE ROLE llmcostcontrol LOGIN PASSWORD ''llmcostcontrol'''
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'llmcostcontrol')\gexec

SELECT 'CREATE DATABASE llmcostcontrol OWNER llmcostcontrol'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'llmcostcontrol')\gexec

GRANT ALL PRIVILEGES ON DATABASE llmcostcontrol TO llmcostcontrol;
