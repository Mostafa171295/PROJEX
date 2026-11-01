-- PROJEX 003: least-privilege grants for the application role. Run as projex_owner.
-- Tables are owned by projex_owner; projex_app can only read/write rows (and RLS applies to it).
GRANT USAGE ON SCHEMA organization, identity, projects TO projex_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA organization, identity, projects TO projex_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA organization, identity, projects TO projex_app;
-- Tables created by later migrations (planning schema etc.) inherit the same grants.
ALTER DEFAULT PRIVILEGES FOR ROLE projex_owner IN SCHEMA organization, identity, projects
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO projex_app;
ALTER DEFAULT PRIVILEGES FOR ROLE projex_owner IN SCHEMA organization, identity, projects
    GRANT USAGE, SELECT ON SEQUENCES TO projex_app;
CREATE SCHEMA IF NOT EXISTS planning;
GRANT USAGE ON SCHEMA planning TO projex_app;
ALTER DEFAULT PRIVILEGES FOR ROLE projex_owner IN SCHEMA planning
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO projex_app;
