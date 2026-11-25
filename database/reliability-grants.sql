-- Apply once as migration owner after canonical database-roles.sql.
GRANT SELECT,INSERT,UPDATE ON vehicle_guard,vehicle_block,school_barrier,school_transition TO v_scheduling_runtime;
GRANT SELECT,INSERT ON gate_command,outbox_message TO v_scheduling_runtime;
GRANT UPDATE(fingerprint,result_json,http_status) ON scheduling_operation TO v_scheduling_runtime;
GRANT UPDATE(inputs_json,basis_json,school_revision) ON scheduling_request TO v_scheduling_runtime;
GRANT UPDATE(revision) ON assessment_appointment TO v_scheduling_runtime;
GRANT UPDATE(is_active) ON lesson_participant TO v_scheduling_runtime;
REVOKE UPDATE ON scheduling_rule_version,candidate_lesson,candidate_lesson_participant FROM v_scheduling_runtime;
DO $$ DECLARE name text; BEGIN
 FOREACH name IN ARRAY ARRAY['vehicle_guard','vehicle_block','gate_command','school_barrier','school_transition','outbox_message'] LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',name);
  EXECUTE format('CREATE POLICY tenant_scope ON %I TO v_scheduling_runtime USING (school_id=nullif(current_setting(''vairas.school_id'',true),'''')::uuid) WITH CHECK (school_id=nullif(current_setting(''vairas.school_id'',true),'''')::uuid)',name);
 END LOOP;
END $$;
GRANT SELECT(id,school_id,kind,teacher_id,branch_id,vehicle_id,student_id,group_id,category_code,starts_at,ends_at,status) ON lesson TO v_scheduling_reservation_authority;
GRANT SELECT(id,school_id,kind,teacher_id,branch_id,vehicle_id,student_id,category_code,starts_at,ends_at,status) ON assessment_appointment TO v_scheduling_reservation_authority;
GRANT SELECT(lesson_id,student_id,is_active) ON lesson_participant TO v_scheduling_reservation_authority;
CREATE POLICY reservation_internal ON lesson_participant FOR SELECT TO v_scheduling_reservation_authority USING(true);
GRANT USAGE ON SCHEMA public TO v_scheduling_outbox;
DO $$ BEGIN EXECUTE format('GRANT CONNECT ON DATABASE %I TO v_scheduling_outbox',current_database()); END $$;
GRANT SELECT ON outbox_message TO v_scheduling_outbox;
GRANT UPDATE(lease_id,lease_until,published_at,available_at,attempts) ON outbox_message TO v_scheduling_outbox;
CREATE POLICY outbox_worker ON outbox_message TO v_scheduling_outbox USING(true) WITH CHECK(true);
REVOKE CREATE ON SCHEMA public FROM v_scheduling_runtime,v_scheduling_reservation_authority,v_scheduling_outbox;
DO $$ BEGIN
 EXECUTE format('REVOKE CREATE,TEMPORARY ON DATABASE %I FROM v_scheduling_runtime,v_scheduling_reservation_authority,v_scheduling_outbox',current_database());
END $$;
