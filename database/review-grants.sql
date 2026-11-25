-- Deactivation reviews (migration DeactivationReview). Apply as migration owner after reliability-grants.sql; re-runnable.
GRANT SELECT,INSERT ON lesson_review,integration_inbox TO v_scheduling_runtime;
GRANT UPDATE(resolved_at,resolved_by_user_id,resolution_note) ON lesson_review TO v_scheduling_runtime;
ALTER TABLE lesson_review ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_policies WHERE schemaname='public' AND tablename='lesson_review' AND policyname='tenant_scope') THEN
  CREATE POLICY tenant_scope ON lesson_review TO v_scheduling_runtime
   USING (school_id=nullif(current_setting('vairas.school_id',true),'')::uuid) WITH CHECK (school_id=nullif(current_setting('vairas.school_id',true),'')::uuid);
 END IF;
END $$;
-- integration_inbox is global (an event can affect several schools) and holds identifiers only; no RLS by design.
REVOKE UPDATE,DELETE,TRUNCATE ON integration_inbox FROM v_scheduling_runtime;
REVOKE DELETE,TRUNCATE ON lesson_review FROM v_scheduling_runtime;
