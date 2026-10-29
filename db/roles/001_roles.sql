-- GoalFlow database roles (ADR-05 2.3). PostgreSQL 15+. Idempotent.
--
-- Run as `postgres` in the Supabase SQL editor.
-- Passwords are assigned separately (alter role ... password ...) and are
-- never stored in this repository.

create schema if not exists goalflow;
revoke create on schema goalflow from public;

do $$
begin
  if not exists (select from pg_roles where rolname = 'goalflow_app') then
    create role goalflow_app login nobypassrls;
  end if;
  if not exists (select from pg_roles where rolname = 'goalflow_backup') then
    create role goalflow_backup login bypassrls;
  end if;
end
$$;

-- Re-assert attributes so a second run converges to the same state.
alter role goalflow_app login nobypassrls;
alter role goalflow_backup login bypassrls;
alter role goalflow_backup set default_transaction_read_only = on;

-- API role: DML only, no DDL.
grant usage on schema goalflow to goalflow_app;
grant select, insert, update, delete on all tables in schema goalflow to goalflow_app;
grant usage, select on all sequences in schema goalflow to goalflow_app;
alter default privileges for role postgres in schema goalflow
  grant select, insert, update, delete on tables to goalflow_app;
alter default privileges for role postgres in schema goalflow
  grant usage, select on sequences to goalflow_app;

-- Backup role: read-only.
grant usage on schema goalflow to goalflow_backup;
grant select on all tables in schema goalflow to goalflow_backup;
grant select on all sequences in schema goalflow to goalflow_backup;
alter default privileges for role postgres in schema goalflow
  grant select on tables to goalflow_backup;
alter default privileges for role postgres in schema goalflow
  grant select on sequences to goalflow_backup;
