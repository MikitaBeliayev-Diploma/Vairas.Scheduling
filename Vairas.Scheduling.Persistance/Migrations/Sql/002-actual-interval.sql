-- G-01: authoritative physical interval, separate from planned time and credited minutes.
-- Run once after initial.sql. Existing Completed rows are preserved, never guessed.
BEGIN;
ALTER TABLE lesson
 ADD COLUMN actual_start timestamptz,
 ADD COLUMN actual_end timestamptz,
 ADD CONSTRAINT lesson_actual_interval CHECK (
   (actual_start IS NULL AND actual_end IS NULL) OR
   (actual_start IS NOT NULL AND actual_end IS NOT NULL AND actual_end > actual_start)),
 ADD CONSTRAINT lesson_completed_requires_actuals CHECK (
   status <> 'Completed' OR (actual_start IS NOT NULL AND actual_end IS NOT NULL)) NOT VALID;

ALTER TABLE lesson_change
 ADD COLUMN previous_actual_start timestamptz,
 ADD COLUMN previous_actual_end timestamptz,
 ADD CONSTRAINT history_actual_interval CHECK (
   (previous_actual_start IS NULL AND previous_actual_end IS NULL) OR
   (previous_actual_start IS NOT NULL AND previous_actual_end IS NOT NULL
    AND previous_actual_end > previous_actual_start));

-- NOT VALID still rejects all new/updated Completed rows without actuals.
-- Validate immediately on empty/clean databases; leave legacy exceptions for reconciliation.
DO $$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM lesson WHERE status='Completed'
                 AND (actual_start IS NULL OR actual_end IS NULL)) THEN
   ALTER TABLE lesson VALIDATE CONSTRAINT lesson_completed_requires_actuals;
 ELSE
   RAISE NOTICE 'G-01: legacy Completed lessons require verified actuals; no credit until reconciled';
 END IF;
END $$;
COMMIT;
