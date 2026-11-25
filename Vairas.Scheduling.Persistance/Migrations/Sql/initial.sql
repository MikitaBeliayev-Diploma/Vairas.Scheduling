CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TYPE lesson_kind AS ENUM ('Practical', 'Theory');
CREATE TYPE lesson_status AS ENUM ('Cancelled', 'Completed', 'NoShow', 'Scheduled');
CREATE TYPE operation_status AS ENUM ('Committed', 'Pending', 'ReconciliationPending', 'Rejected');
CREATE TYPE recurrence_frequency AS ENUM ('Daily', 'Weekly');
CREATE TYPE request_status AS ENUM ('Confirmed', 'Draft', 'Infeasible', 'Ready', 'Rejected', 'Stale', 'Submitted', 'Superseded');
CREATE TYPE reservation_status AS ENUM ('Active', 'Released');
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE assessment_appointment (
    id uuid NOT NULL,
    school_id uuid NOT NULL,
    kind lesson_kind NOT NULL,
    enrollment_id uuid NOT NULL,
    student_id uuid NOT NULL,
    assessment_definition_id uuid NOT NULL,
    outstanding_requirement_id uuid,
    reason character varying(500) NOT NULL,
    teacher_id uuid NOT NULL,
    vehicle_id uuid,
    branch_id uuid NOT NULL,
    category_code character varying(16) NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    status lesson_status NOT NULL DEFAULT 'Scheduled'::lesson_status,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_assessment_appointment" PRIMARY KEY (id),
    CONSTRAINT "AK_assessment_appointment_id_school_id" UNIQUE (id, school_id),
    CONSTRAINT assessment_interval CHECK (ends_at > starts_at),
    CONSTRAINT assessment_type CHECK ((kind = 'Practical' AND vehicle_id IS NOT NULL) OR (kind = 'Theory' AND vehicle_id IS NULL))
);

CREATE TABLE recurring_lesson_series (
    id uuid NOT NULL,
    school_id uuid NOT NULL,
    kind lesson_kind NOT NULL,
    frequency recurrence_frequency NOT NULL,
    recurrence_interval integer NOT NULL DEFAULT 1,
    weekdays smallint[],
    local_start_time time NOT NULL,
    duration_minutes integer NOT NULL,
    time_zone character varying(80) NOT NULL,
    starts_on date NOT NULL,
    ends_on date,
    occurrence_count integer,
    CONSTRAINT "PK_recurring_lesson_series" PRIMARY KEY (id),
    CONSTRAINT "AK_recurring_lesson_series_id_school_id_kind" UNIQUE (id, school_id, kind),
    CONSTRAINT recurrence_count CHECK (occurrence_count IS NULL OR occurrence_count > 0),
    CONSTRAINT recurrence_end_choice CHECK ((ends_on IS NOT NULL)::int + (occurrence_count IS NOT NULL)::int = 1),
    CONSTRAINT recurrence_end_range CHECK (ends_on IS NULL OR ends_on >= starts_on),
    CONSTRAINT recurrence_positive CHECK (recurrence_interval > 0 AND duration_minutes > 0),
    CONSTRAINT recurrence_weekdays CHECK ((frequency = 'Daily' AND coalesce(cardinality(weekdays),0) = 0) OR (frequency = 'Weekly' AND weekdays IS NOT NULL AND cardinality(weekdays) BETWEEN 1 AND 7 AND weekdays <@ ARRAY[1,2,3,4,5,6,7]::smallint[]))
);

CREATE TABLE scheduling_rule_version (
    id uuid NOT NULL,
    rule_id uuid NOT NULL,
    revision integer NOT NULL,
    source_kind character varying(16) NOT NULL,
    school_id uuid,
    category_code character varying(16),
    rule_code character varying(80) NOT NULL,
    severity character varying(16) NOT NULL,
    override_policy character varying(24) NOT NULL,
    scope_kind character varying(24) NOT NULL,
    scope_resource_id uuid,
    decision_reason character varying(500),
    numeric_limit numeric(12,3),
    unit character varying(32),
    effective_from timestamp with time zone NOT NULL,
    effective_to timestamp with time zone,
    source_reference character varying(400) NOT NULL,
    verification_status character varying(24) NOT NULL,
    decided_at timestamp with time zone,
    created_by_user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    decided_by_user_id uuid,
    CONSTRAINT "PK_scheduling_rule_version" PRIMARY KEY (id),
    CONSTRAINT rule_decision_actor CHECK ((verification_status = 'Pending' AND decided_at IS NULL AND decided_by_user_id IS NULL) OR (verification_status IN ('Verified','Rejected') AND decided_at IS NOT NULL AND decided_by_user_id IS NOT NULL)),
    CONSTRAINT rule_effective_interval CHECK (effective_to IS NULL OR effective_to > effective_from),
    CONSTRAINT rule_override_policy CHECK (override_policy IN ('Locked','SchoolEditable') AND ((source_kind = 'Legal' AND override_policy = 'Locked') OR (source_kind = 'School' AND override_policy = 'SchoolEditable'))),
    CONSTRAINT rule_revision CHECK (revision > 0),
    CONSTRAINT rule_school_scope CHECK ((source_kind = 'Legal' AND school_id IS NULL) OR (source_kind = 'School' AND school_id IS NOT NULL)),
    CONSTRAINT rule_scope CHECK (scope_kind IN ('School','Instructor','Vehicle','Branch','Group','Student') AND ((scope_kind = 'School' AND scope_resource_id IS NULL) OR (scope_kind <> 'School' AND scope_resource_id IS NOT NULL))),
    CONSTRAINT rule_severity CHECK (severity IN ('Hard','Preference') AND (source_kind <> 'Legal' OR severity = 'Hard')),
    CONSTRAINT rule_source_kind CHECK (source_kind IN ('Legal','School')),
    CONSTRAINT rule_typed_limit CHECK ((numeric_limit IS NULL AND unit IS NULL) OR (numeric_limit > 0 AND unit IS NOT NULL)),
    CONSTRAINT rule_verification CHECK (verification_status IN ('Verified','Pending','Rejected'))
);

CREATE TABLE candidate_lesson (
    id uuid NOT NULL,
    request_id uuid NOT NULL,
    request_revision integer NOT NULL,
    kind lesson_kind NOT NULL,
    student_id uuid,
    enrollment_id uuid,
    group_id uuid,
    teacher_id uuid NOT NULL,
    vehicle_id uuid,
    branch_id uuid NOT NULL,
    series_id uuid,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    category_code character varying(16) NOT NULL,
    CONSTRAINT "PK_candidate_lesson" PRIMARY KEY (id),
    CONSTRAINT "AK_candidate_lesson_id_request_id_request_revision" UNIQUE (id, request_id, request_revision),
    CONSTRAINT candidate_interval CHECK (ends_at > starts_at),
    CONSTRAINT candidate_type CHECK ((kind = 'Practical' AND enrollment_id IS NOT NULL AND student_id IS NOT NULL AND vehicle_id IS NOT NULL AND group_id IS NULL) OR (kind = 'Theory' AND group_id IS NOT NULL AND enrollment_id IS NULL AND student_id IS NULL AND vehicle_id IS NULL)),
    CONSTRAINT "FK_candidate_lesson_recurring_lesson_series_series_id" FOREIGN KEY (series_id) REFERENCES recurring_lesson_series (id) ON DELETE RESTRICT
);

CREATE TABLE candidate_lesson_participant (
    candidate_lesson_id uuid NOT NULL,
    enrollment_id uuid NOT NULL,
    student_id uuid NOT NULL,
    membership_revision bigint NOT NULL,
    CONSTRAINT "PK_candidate_lesson_participant" PRIMARY KEY (candidate_lesson_id, enrollment_id),
    CONSTRAINT candidate_lesson_participant_membership_revision CHECK (membership_revision > 0),
    CONSTRAINT "FK_candidate_lesson_participant_candidate_lesson_candidate_les~" FOREIGN KEY (candidate_lesson_id) REFERENCES candidate_lesson (id) ON DELETE RESTRICT
);

CREATE TABLE lesson (
    id uuid NOT NULL,
    school_id uuid NOT NULL,
    kind lesson_kind NOT NULL,
    branch_id uuid NOT NULL,
    teacher_id uuid NOT NULL,
    enrollment_id uuid,
    student_id uuid,
    vehicle_id uuid,
    group_id uuid,
    group_membership_revision bigint,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    status lesson_status NOT NULL DEFAULT 'Scheduled'::lesson_status,
    revision integer NOT NULL DEFAULT 1,
    series_id uuid,
    source_candidate_id uuid,
    created_at timestamp with time zone NOT NULL,
    category_code character varying(16) NOT NULL,
    CONSTRAINT "PK_lesson" PRIMARY KEY (id),
    CONSTRAINT "AK_lesson_id_school_id" UNIQUE (id, school_id),
    CONSTRAINT lesson_interval CHECK (ends_at > starts_at),
    CONSTRAINT lesson_membership_basis CHECK ((kind = 'Practical' AND group_membership_revision IS NULL) OR (kind = 'Theory' AND group_membership_revision IS NOT NULL AND group_membership_revision > 0)),
    CONSTRAINT lesson_revision_positive CHECK (revision > 0),
    CONSTRAINT lesson_type CHECK ((kind = 'Practical' AND enrollment_id IS NOT NULL AND student_id IS NOT NULL AND vehicle_id IS NOT NULL AND group_id IS NULL) OR (kind = 'Theory' AND group_id IS NOT NULL AND enrollment_id IS NULL AND student_id IS NULL AND vehicle_id IS NULL)),
    CONSTRAINT "FK_lesson_candidate_lesson_source_candidate_id" FOREIGN KEY (source_candidate_id) REFERENCES candidate_lesson (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_lesson_recurring_lesson_series_series_id_school_id_kind" FOREIGN KEY (series_id, school_id, kind) REFERENCES recurring_lesson_series (id, school_id, kind) ON DELETE RESTRICT
);

CREATE TABLE lesson_participant (
    lesson_id uuid NOT NULL,
    enrollment_id uuid NOT NULL,
    student_id uuid NOT NULL,
    membership_revision bigint NOT NULL,
    CONSTRAINT "PK_lesson_participant" PRIMARY KEY (lesson_id, enrollment_id),
    CONSTRAINT lesson_participant_membership_revision CHECK (membership_revision > 0),
    CONSTRAINT "FK_lesson_participant_lesson_lesson_id" FOREIGN KEY (lesson_id) REFERENCES lesson (id) ON DELETE RESTRICT
);

CREATE TABLE lesson_change (
    id uuid NOT NULL,
    school_id uuid NOT NULL,
    lesson_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    revision integer NOT NULL,
    previous_status lesson_status NOT NULL,
    previous_start timestamp with time zone NOT NULL,
    previous_end timestamp with time zone NOT NULL,
    previous_teacher_id uuid NOT NULL,
    previous_vehicle_id uuid,
    previous_branch_id uuid NOT NULL,
    previous_series_id uuid,
    previous_group_membership_revision bigint,
    changed_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_lesson_change" PRIMARY KEY (id),
    CONSTRAINT history_previous_interval CHECK (previous_end > previous_start),
    CONSTRAINT history_revision_positive CHECK (revision > 1),
    CONSTRAINT "FK_lesson_change_lesson_lesson_id_school_id" FOREIGN KEY (lesson_id, school_id) REFERENCES lesson (id, school_id) ON DELETE RESTRICT
);

CREATE TABLE reservation_claim (
    id uuid NOT NULL,
    operation_id uuid NOT NULL,
    lesson_id uuid,
    assessment_appointment_id uuid,
    school_id uuid NOT NULL,
    resource_kind character varying(24) NOT NULL,
    resource_key uuid NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    status reservation_status NOT NULL DEFAULT 'Active'::reservation_status,
    created_at timestamp with time zone NOT NULL,
    released_at timestamp with time zone,
    CONSTRAINT "PK_reservation_claim" PRIMARY KEY (id),
    CONSTRAINT claim_interval CHECK (ends_at > starts_at),
    CONSTRAINT claim_kind CHECK (resource_kind IN ('Student','Instructor','Vehicle','Group')),
    CONSTRAINT claim_owner CHECK ((lesson_id IS NOT NULL)::int + (assessment_appointment_id IS NOT NULL)::int = 1),
    CONSTRAINT claim_release CHECK ((status = 'Active' AND released_at IS NULL) OR (status = 'Released' AND released_at IS NOT NULL AND released_at >= created_at)),
    CONSTRAINT "FK_reservation_claim_assessment_appointment_assessment_appoint~" FOREIGN KEY (assessment_appointment_id, school_id) REFERENCES assessment_appointment (id, school_id) ON DELETE RESTRICT,
    CONSTRAINT "FK_reservation_claim_lesson_lesson_id_school_id" FOREIGN KEY (lesson_id, school_id) REFERENCES lesson (id, school_id) ON DELETE RESTRICT
);

CREATE TABLE scheduling_operation (
    id uuid NOT NULL,
    school_id uuid NOT NULL,
    action_type character varying(32) NOT NULL,
    request_id uuid,
    request_revision integer,
    actor_user_id uuid NOT NULL,
    channel character varying(16) NOT NULL,
    reason character varying(500),
    status operation_status NOT NULL DEFAULT 'Pending'::operation_status,
    started_at timestamp with time zone NOT NULL,
    finished_at timestamp with time zone,
    target_lesson_id uuid,
    expected_lesson_revision integer,
    target_appointment_id uuid,
    result_lesson_id uuid,
    result_appointment_id uuid,
    outcome_code character varying(64),
    user_confirmed_at timestamp with time zone,
    CONSTRAINT "PK_scheduling_operation" PRIMARY KEY (id),
    CONSTRAINT "AK_scheduling_operation_id_school_id" UNIQUE (id, school_id),
    CONSTRAINT operation_action CHECK (action_type IN ('ConfirmSchedule','CreateLesson','Reschedule','Cancel','ChangeGroup','CompleteLesson','CreateAssessment','CancelAssessment')),
    CONSTRAINT operation_channel CHECK (channel IN ('Web','MCP','Internal')),
    CONSTRAINT operation_expected_revision CHECK (expected_lesson_revision IS NULL OR expected_lesson_revision > 0),
    CONSTRAINT operation_finished CHECK ((status = 'Pending' AND finished_at IS NULL) OR (status = 'ReconciliationPending') OR (status IN ('Committed','Rejected') AND finished_at IS NOT NULL)),
    CONSTRAINT operation_request_pair CHECK ((request_id IS NULL) = (request_revision IS NULL)),
    CONSTRAINT operation_target_xor CHECK (target_lesson_id IS NULL OR target_appointment_id IS NULL),
    CONSTRAINT "FK_scheduling_operation_assessment_appointment_result_appointm~" FOREIGN KEY (result_appointment_id, school_id) REFERENCES assessment_appointment (id, school_id) ON DELETE RESTRICT,
    CONSTRAINT "FK_scheduling_operation_assessment_appointment_target_appointm~" FOREIGN KEY (target_appointment_id, school_id) REFERENCES assessment_appointment (id, school_id) ON DELETE RESTRICT,
    CONSTRAINT "FK_scheduling_operation_lesson_result_lesson_id_school_id" FOREIGN KEY (result_lesson_id, school_id) REFERENCES lesson (id, school_id) ON DELETE RESTRICT,
    CONSTRAINT "FK_scheduling_operation_lesson_target_lesson_id_school_id" FOREIGN KEY (target_lesson_id, school_id) REFERENCES lesson (id, school_id) ON DELETE RESTRICT
);

CREATE TABLE scheduling_operation_rule (
    operation_id uuid NOT NULL,
    rule_version_id uuid NOT NULL,
    CONSTRAINT "PK_scheduling_operation_rule" PRIMARY KEY (operation_id, rule_version_id),
    CONSTRAINT "FK_scheduling_operation_rule_scheduling_operation_operation_id" FOREIGN KEY (operation_id) REFERENCES scheduling_operation (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_scheduling_operation_rule_scheduling_rule_version_rule_vers~" FOREIGN KEY (rule_version_id) REFERENCES scheduling_rule_version (id) ON DELETE RESTRICT
);

CREATE TABLE scheduling_request (
    id uuid NOT NULL,
    revision integer NOT NULL,
    school_id uuid NOT NULL,
    status request_status NOT NULL DEFAULT 'Draft'::request_status,
    planning_start date,
    planning_end date,
    submitted_by_user_id uuid,
    submitted_at timestamp with time zone,
    generated_at timestamp with time zone,
    confirmed_operation_id uuid,
    confirmed_at timestamp with time zone,
    created_by_user_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_scheduling_request" PRIMARY KEY (id, revision),
    CONSTRAINT "AK_scheduling_request_id_revision_school_id" UNIQUE (id, revision, school_id),
    CONSTRAINT request_confirmed_metadata CHECK ((status = 'Confirmed' AND confirmed_at IS NOT NULL AND confirmed_operation_id IS NOT NULL) OR (status <> 'Confirmed' AND confirmed_at IS NULL AND confirmed_operation_id IS NULL)),
    CONSTRAINT request_period CHECK (planning_start IS NULL OR planning_end IS NULL OR planning_end >= planning_start),
    CONSTRAINT request_ready_metadata CHECK (status NOT IN ('Ready','Confirmed') OR generated_at IS NOT NULL),
    CONSTRAINT request_revision_positive CHECK (revision > 0),
    CONSTRAINT request_submitted_pair CHECK ((submitted_at IS NULL) = (submitted_by_user_id IS NULL)),
    CONSTRAINT request_validated_metadata CHECK (status NOT IN ('Submitted','Ready','Infeasible','Stale','Superseded','Confirmed') OR (submitted_at IS NOT NULL AND planning_start IS NOT NULL AND planning_end IS NOT NULL)),
    CONSTRAINT "FK_scheduling_request_scheduling_operation_confirmed_operation~" FOREIGN KEY (confirmed_operation_id, school_id) REFERENCES scheduling_operation (id, school_id) ON DELETE RESTRICT
);

CREATE TABLE scheduling_request_enrollment (
    request_id uuid NOT NULL,
    request_revision integer NOT NULL,
    enrollment_id uuid NOT NULL,
    student_id uuid,
    enrollment_revision bigint,
    training_program_version_id uuid,
    category_code character varying(16),
    CONSTRAINT "PK_scheduling_request_enrollment" PRIMARY KEY (request_id, request_revision, enrollment_id),
    CONSTRAINT scheduling_request_enrollment_revision CHECK (request_revision > 0),
    CONSTRAINT "FK_scheduling_request_enrollment_scheduling_request_request_id~" FOREIGN KEY (request_id, request_revision) REFERENCES scheduling_request (id, revision) ON DELETE RESTRICT
);

CREATE TABLE scheduling_request_group (
    request_id uuid NOT NULL,
    request_revision integer NOT NULL,
    group_id uuid NOT NULL,
    training_program_version_id uuid,
    category_code character varying(16),
    membership_revision bigint,
    CONSTRAINT "PK_scheduling_request_group" PRIMARY KEY (request_id, request_revision, group_id),
    CONSTRAINT scheduling_request_group_revision CHECK (request_revision > 0),
    CONSTRAINT "FK_scheduling_request_group_scheduling_request_request_id_requ~" FOREIGN KEY (request_id, request_revision) REFERENCES scheduling_request (id, revision) ON DELETE RESTRICT
);

CREATE TABLE scheduling_request_resource (
    request_id uuid NOT NULL,
    request_revision integer NOT NULL,
    resource_kind character varying(24) NOT NULL,
    resource_id uuid NOT NULL,
    eligibility_revision bigint,
    CONSTRAINT "PK_scheduling_request_resource" PRIMARY KEY (request_id, request_revision, resource_kind, resource_id),
    CONSTRAINT resource_kind_valid CHECK (resource_kind IN ('Instructor','Vehicle','Branch')),
    CONSTRAINT resource_source_revision CHECK (eligibility_revision IS NULL OR eligibility_revision > 0),
    CONSTRAINT scheduling_request_resource_revision CHECK (request_revision > 0),
    CONSTRAINT "FK_scheduling_request_resource_scheduling_request_request_id_r~" FOREIGN KEY (request_id, request_revision) REFERENCES scheduling_request (id, revision) ON DELETE RESTRICT
);

CREATE TABLE scheduling_request_rule (
    request_id uuid NOT NULL,
    request_revision integer NOT NULL,
    rule_version_id uuid NOT NULL,
    CONSTRAINT "PK_scheduling_request_rule" PRIMARY KEY (request_id, request_revision, rule_version_id),
    CONSTRAINT scheduling_request_rule_revision CHECK (request_revision > 0),
    CONSTRAINT "FK_scheduling_request_rule_scheduling_request_request_id_reque~" FOREIGN KEY (request_id, request_revision) REFERENCES scheduling_request (id, revision) ON DELETE RESTRICT,
    CONSTRAINT "FK_scheduling_request_rule_scheduling_rule_version_rule_versio~" FOREIGN KEY (rule_version_id) REFERENCES scheduling_rule_version (id) ON DELETE RESTRICT
);

CREATE TABLE scheduling_request_window (
    id uuid NOT NULL,
    request_id uuid NOT NULL,
    request_revision integer NOT NULL,
    starts_at timestamp with time zone NOT NULL,
    ends_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_scheduling_request_window" PRIMARY KEY (id),
    CONSTRAINT window_interval CHECK (ends_at > starts_at),
    CONSTRAINT "FK_scheduling_request_window_scheduling_request_request_id_req~" FOREIGN KEY (request_id, request_revision) REFERENCES scheduling_request (id, revision) ON DELETE RESTRICT
);

CREATE INDEX "IX_candidate_lesson_request_id_request_revision" ON candidate_lesson (request_id, request_revision);

CREATE INDEX "IX_candidate_lesson_series_id" ON candidate_lesson (series_id);

CREATE UNIQUE INDEX "IX_candidate_lesson_participant_candidate_lesson_id_student_id" ON candidate_lesson_participant (candidate_lesson_id, student_id);

CREATE INDEX "IX_lesson_school_id_starts_at" ON lesson (school_id, starts_at);

CREATE INDEX "IX_lesson_series_id_school_id_kind" ON lesson (series_id, school_id, kind);

CREATE UNIQUE INDEX "IX_lesson_source_candidate_id" ON lesson (source_candidate_id);

CREATE INDEX "IX_lesson_teacher_id_starts_at_ends_at" ON lesson (teacher_id, starts_at, ends_at);

CREATE UNIQUE INDEX "IX_lesson_change_lesson_id_revision" ON lesson_change (lesson_id, revision);

CREATE INDEX "IX_lesson_change_lesson_id_school_id" ON lesson_change (lesson_id, school_id);

CREATE INDEX "IX_lesson_change_operation_id_school_id" ON lesson_change (operation_id, school_id);

CREATE UNIQUE INDEX "IX_lesson_participant_lesson_id_student_id" ON lesson_participant (lesson_id, student_id);

CREATE INDEX "IX_reservation_claim_assessment_appointment_id_school_id" ON reservation_claim (assessment_appointment_id, school_id);

CREATE INDEX "IX_reservation_claim_lesson_id_resource_kind_resource_key" ON reservation_claim (lesson_id, resource_kind, resource_key);

CREATE INDEX "IX_reservation_claim_lesson_id_school_id" ON reservation_claim (lesson_id, school_id);

CREATE INDEX "IX_reservation_claim_operation_id_school_id" ON reservation_claim (operation_id, school_id);

CREATE INDEX "IX_reservation_claim_resource_kind_resource_key_starts_at_ends~" ON reservation_claim (resource_kind, resource_key, starts_at, ends_at);

CREATE INDEX "IX_scheduling_operation_request_id_request_revision_school_id" ON scheduling_operation (request_id, request_revision, school_id);

CREATE INDEX "IX_scheduling_operation_result_appointment_id_school_id" ON scheduling_operation (result_appointment_id, school_id);

CREATE INDEX "IX_scheduling_operation_result_lesson_id_school_id" ON scheduling_operation (result_lesson_id, school_id);

CREATE INDEX "IX_scheduling_operation_target_appointment_id_school_id" ON scheduling_operation (target_appointment_id, school_id);

CREATE INDEX "IX_scheduling_operation_target_lesson_id_school_id" ON scheduling_operation (target_lesson_id, school_id);

CREATE INDEX "IX_scheduling_operation_rule_rule_version_id" ON scheduling_operation_rule (rule_version_id);

CREATE INDEX "IX_scheduling_request_confirmed_operation_id_school_id" ON scheduling_request (confirmed_operation_id, school_id);

CREATE INDEX "IX_scheduling_request_school_id_status" ON scheduling_request (school_id, status);

CREATE INDEX "IX_scheduling_request_rule_rule_version_id" ON scheduling_request_rule (rule_version_id);

CREATE INDEX "IX_scheduling_request_window_request_id_request_revision" ON scheduling_request_window (request_id, request_revision);

CREATE UNIQUE INDEX "IX_scheduling_rule_version_rule_id_revision" ON scheduling_rule_version (rule_id, revision);

CREATE INDEX "IX_scheduling_rule_version_source_kind_school_id_rule_code" ON scheduling_rule_version (source_kind, school_id, rule_code);

ALTER TABLE candidate_lesson ADD CONSTRAINT "FK_candidate_lesson_scheduling_request_request_id_request_revi~" FOREIGN KEY (request_id, request_revision) REFERENCES scheduling_request (id, revision) ON DELETE RESTRICT;

ALTER TABLE lesson_change ADD CONSTRAINT "FK_lesson_change_scheduling_operation_operation_id_school_id" FOREIGN KEY (operation_id, school_id) REFERENCES scheduling_operation (id, school_id) ON DELETE RESTRICT;

ALTER TABLE reservation_claim ADD CONSTRAINT "FK_reservation_claim_scheduling_operation_operation_id_school_~" FOREIGN KEY (operation_id, school_id) REFERENCES scheduling_operation (id, school_id) ON DELETE RESTRICT;

ALTER TABLE scheduling_operation ADD CONSTRAINT "FK_scheduling_operation_scheduling_request_request_id_request_~" FOREIGN KEY (request_id, request_revision, school_id) REFERENCES scheduling_request (id, revision, school_id) ON DELETE RESTRICT;

ALTER TABLE scheduling_request ADD CONSTRAINT request_single_school
EXCLUDE USING gist (id WITH =, school_id WITH <>);
ALTER TABLE reservation_claim ADD CONSTRAINT active_resource_no_overlap
EXCLUDE USING gist (resource_kind WITH =, resource_key WITH =,
  tstzrange(starts_at, ends_at, '[)') WITH &&) WHERE (status = 'Active');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261115192213_InitialScheduling', '10.0.4');

COMMIT;
