CREATE FUNCTION public.scheduling_immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 RAISE EXCEPTION 'Scheduling evidence is immutable' USING ERRCODE='23514';
END $$;
CREATE TRIGGER history_immutable BEFORE UPDATE OR DELETE ON lesson_change FOR EACH ROW EXECUTE FUNCTION public.scheduling_immutable();
CREATE TRIGGER rule_immutable BEFORE UPDATE OR DELETE ON scheduling_rule_version FOR EACH ROW EXECUTE FUNCTION public.scheduling_immutable();
CREATE TRIGGER rule_evidence_immutable BEFORE UPDATE OR DELETE ON scheduling_operation_rule FOR EACH ROW EXECUTE FUNCTION public.scheduling_immutable();
CREATE TRIGGER candidate_immutable BEFORE UPDATE OR DELETE ON candidate_lesson FOR EACH ROW EXECUTE FUNCTION public.scheduling_immutable();
CREATE TRIGGER candidate_roster_immutable BEFORE UPDATE OR DELETE ON candidate_lesson_participant FOR EACH ROW EXECUTE FUNCTION public.scheduling_immutable();
CREATE TRIGGER gate_command_immutable BEFORE UPDATE OR DELETE ON gate_command FOR EACH ROW EXECUTE FUNCTION public.scheduling_immutable();
CREATE FUNCTION public.scheduling_operation_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 IF OLD.status IN ('Committed','Rejected') OR
   (to_jsonb(NEW)-ARRAY['status','finished_at','result_lesson_id','result_appointment_id','outcome_code','result_json','http_status'])
   IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','finished_at','result_lesson_id','result_appointment_id','outcome_code','result_json','http_status']) THEN
  RAISE EXCEPTION 'Immutable command identity or terminal outcome' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER operation_guard BEFORE UPDATE ON scheduling_operation FOR EACH ROW EXECUTE FUNCTION public.scheduling_operation_guard();
CREATE FUNCTION public.scheduling_request_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 IF OLD.status <> 'Draft' AND (to_jsonb(NEW)-ARRAY['status','generated_at','confirmed_at','confirmed_operation_id'])
 IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','generated_at','confirmed_at','confirmed_operation_id']) THEN
  RAISE EXCEPTION 'Submitted inputs are immutable' USING ERRCODE='23514';
 END IF;
 IF OLD.status='Confirmed' THEN RAISE EXCEPTION 'Confirmed request is immutable' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER request_guard BEFORE UPDATE ON scheduling_request FOR EACH ROW EXECUTE FUNCTION public.scheduling_request_guard();
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC;
