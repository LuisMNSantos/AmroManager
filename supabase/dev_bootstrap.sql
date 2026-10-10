-- ============================================================
-- AMRO Manager – Dev database bootstrap
-- Run once in the Supabase SQL Editor on your dev project.
-- Safe to re-run: tables use CREATE TABLE IF NOT EXISTS,
-- policies use CREATE POLICY IF NOT EXISTS (Supabase ≥ 2.x).
-- ============================================================


-- ── 0. DROP EXISTING TABLES ────────────────────────────────
-- CASCADE drops dependent FK constraints automatically.
-- Order doesn't matter because of CASCADE.

DROP TABLE IF EXISTS public.renewer_kit_delivery_items CASCADE;
DROP TABLE IF EXISTS public.renewer_kit_deliveries     CASCADE;
DROP TABLE IF EXISTS public.renewer_kit_items          CASCADE;
DROP TABLE IF EXISTS public.reimbursements             CASCADE;
DROP TABLE IF EXISTS public.distribution_records       CASCADE;
DROP TABLE IF EXISTS public.distribution_campaigns     CASCADE;
DROP TABLE IF EXISTS public.stock_movements            CASCADE;
DROP TABLE IF EXISTS public.size_variants              CASCADE;
DROP TABLE IF EXISTS public.products                   CASCADE;
DROP TABLE IF EXISTS public.general_item_loans         CASCADE;
DROP TABLE IF EXISTS public.general_items              CASCADE;
DROP TABLE IF EXISTS public.bis_loans                  CASCADE;
DROP TABLE IF EXISTS public.pending_registrations      CASCADE;
DROP TABLE IF EXISTS public.audit_logs                 CASCADE;
DROP TABLE IF EXISTS public.visits                     CASCADE;
DROP TABLE IF EXISTS public.reservations               CASCADE;
DROP TABLE IF EXISTS public.deliveries                 CASCADE;
DROP TABLE IF EXISTS public.residents                  CASCADE;
DROP TABLE IF EXISTS public.rooms                      CASCADE;

DROP FUNCTION IF EXISTS public.get_overnight_totals(jsonb);


-- ── 1. TABLES ──────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS public.products (
  sync_id    text      NOT NULL,
  name       text      NOT NULL DEFAULT '',
  type       text      NOT NULL DEFAULT '',
  color      text      NOT NULL DEFAULT '',
  sku        text      NOT NULL DEFAULT '',
  created_at timestamptz NOT NULL DEFAULT now(),
  is_deleted boolean   NOT NULL DEFAULT false,
  updated_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT products_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.size_variants (
  sync_id          text    NOT NULL,
  product_sync_id  text    NOT NULL,
  size             text    NOT NULL DEFAULT '',
  quantity         integer NOT NULL DEFAULT 0,
  min_stock_alert  integer NOT NULL DEFAULT 5,
  is_deleted       boolean NOT NULL DEFAULT false,
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT size_variants_pkey PRIMARY KEY (sync_id),
  CONSTRAINT size_variants_product_sync_id_fkey
    FOREIGN KEY (product_sync_id) REFERENCES public.products(sync_id)
);

CREATE TABLE IF NOT EXISTS public.stock_movements (
  sync_id              text    NOT NULL,
  size_variant_sync_id text    NOT NULL,
  change_amount        integer NOT NULL DEFAULT 0,
  reason               integer NOT NULL DEFAULT 0,
  room_number          text,
  notes                text,
  date                 timestamptz NOT NULL DEFAULT now(),
  is_deleted           boolean NOT NULL DEFAULT false,
  updated_at           timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT stock_movements_pkey PRIMARY KEY (sync_id),
  CONSTRAINT stock_movements_size_variant_sync_id_fkey
    FOREIGN KEY (size_variant_sync_id) REFERENCES public.size_variants(sync_id)
);

CREATE TABLE IF NOT EXISTS public.general_items (
  sync_id        text    NOT NULL,
  name           text    NOT NULL DEFAULT '',
  total_quantity integer NOT NULL DEFAULT 1,
  description    text,
  is_deleted     boolean NOT NULL DEFAULT false,
  updated_at     timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT general_items_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.general_item_loans (
  sync_id              text    NOT NULL,
  general_item_sync_id text    NOT NULL,
  room_number          text    NOT NULL DEFAULT '',
  given_by             text    NOT NULL DEFAULT '',
  quantity             integer NOT NULL DEFAULT 1,
  loan_date            timestamptz NOT NULL DEFAULT now(),
  return_date          timestamptz,
  notes                text,
  is_deleted           boolean NOT NULL DEFAULT false,
  updated_at           timestamptz NOT NULL DEFAULT now(),
  is_returned          boolean NOT NULL DEFAULT false,
  CONSTRAINT general_item_loans_pkey PRIMARY KEY (sync_id),
  CONSTRAINT general_item_loans_general_item_sync_id_fkey
    FOREIGN KEY (general_item_sync_id) REFERENCES public.general_items(sync_id)
);

CREATE TABLE IF NOT EXISTS public.residents (
  sync_id          text    NOT NULL,
  name             text    NOT NULL DEFAULT '',
  room_number      text    NOT NULL DEFAULT '',
  phone_number     text,
  is_collaborator  boolean NOT NULL DEFAULT false,
  is_deleted       boolean NOT NULL DEFAULT false,
  updated_at       timestamptz NOT NULL DEFAULT now(),
  collaborator_role text,
  is_renewer       boolean DEFAULT false,
  free_overnights  integer DEFAULT 0,
  moved_in_at      timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT residents_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.reservations (
  sync_id                  text    NOT NULL,
  space                    integer NOT NULL DEFAULT 0,
  room_number              text    NOT NULL DEFAULT '',
  reserved_by              text    NOT NULL DEFAULT '',
  start_time               timestamptz NOT NULL DEFAULT now(),
  end_time                 timestamptz NOT NULL DEFAULT now(),
  notes                    text,
  created_at               timestamptz NOT NULL DEFAULT now(),
  is_cancelled             boolean NOT NULL DEFAULT false,
  is_deleted               boolean NOT NULL DEFAULT false,
  updated_at               timestamptz NOT NULL DEFAULT now(),
  is_activated             boolean NOT NULL DEFAULT false,
  is_completed             boolean NOT NULL DEFAULT false,
  access_card_loan_sync_id text,
  CONSTRAINT reservations_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.deliveries (
  sync_id       text    NOT NULL,
  type          integer NOT NULL DEFAULT 0,
  room_number   text    NOT NULL DEFAULT '',
  quantity      integer NOT NULL DEFAULT 1,
  arrived_at    timestamptz NOT NULL DEFAULT now(),
  collected_at  timestamptz,
  notes         text,
  is_deleted    boolean NOT NULL DEFAULT false,
  updated_at    timestamptz NOT NULL DEFAULT now(),
  is_delivered  boolean NOT NULL DEFAULT false,
  registered_by text,
  CONSTRAINT deliveries_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.distribution_campaigns (
  sync_id              text    NOT NULL,
  name                 text    NOT NULL,
  size_variant_sync_id text    NOT NULL,
  variant_label        text    NOT NULL,
  quantity_per_room    integer NOT NULL DEFAULT 1,
  total_rooms          integer NOT NULL DEFAULT 0,
  delivered_count      integer NOT NULL DEFAULT 0,
  notes                text,
  created_at           timestamptz NOT NULL DEFAULT now(),
  is_deleted           boolean NOT NULL DEFAULT false,
  updated_at           timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT distribution_campaigns_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.distribution_records (
  sync_id          text NOT NULL,
  campaign_sync_id text NOT NULL,
  room_number      text NOT NULL,
  distributed_at   timestamptz,
  is_deleted       boolean NOT NULL DEFAULT false,
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT distribution_records_pkey PRIMARY KEY (sync_id),
  CONSTRAINT distribution_records_campaign_sync_id_fkey
    FOREIGN KEY (campaign_sync_id) REFERENCES public.distribution_campaigns(sync_id)
);

CREATE TABLE IF NOT EXISTS public.rooms (
  number      text NOT NULL,
  code        text NOT NULL,
  designation text NOT NULL,
  visit_pin   text,
  CONSTRAINT rooms_pkey PRIMARY KEY (number)
);

CREATE TABLE IF NOT EXISTS public.bis_loans (
  sync_id     text    NOT NULL,
  room_number text    NOT NULL,
  given_by    text    NOT NULL,
  loan_date   timestamptz NOT NULL,
  return_date timestamptz,
  is_returned boolean NOT NULL DEFAULT false,
  notes       text,
  is_deleted  boolean NOT NULL DEFAULT false,
  updated_at  timestamptz NOT NULL,
  CONSTRAINT bis_loans_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.pending_registrations (
  id           uuid NOT NULL DEFAULT gen_random_uuid(),
  name         text NOT NULL,
  room_number  text NOT NULL,
  phone_number text,
  notes        text,
  requested_at timestamptz DEFAULT now(),
  CONSTRAINT pending_registrations_pkey PRIMARY KEY (id)
);

-- sync_id is set by the app; id is the DB-generated UUID primary key.
CREATE TABLE IF NOT EXISTS public.audit_logs (
  sync_id      text NOT NULL,
  id           uuid NOT NULL DEFAULT gen_random_uuid(),
  action       text NOT NULL,
  details      text,
  performed_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT audit_logs_pkey        PRIMARY KEY (id),
  CONSTRAINT audit_logs_sync_id_key UNIQUE (sync_id)
);

-- created_at / updated_at have no DEFAULT — the app sets them explicitly.
CREATE TABLE IF NOT EXISTS public.visits (
  sync_id                  text    NOT NULL,
  visitor_name             text    NOT NULL,
  room_number              text    NOT NULL,
  registered_by            text,
  checked_in_at            timestamptz NOT NULL,
  checked_out_at           timestamptz,
  overnights               integer NOT NULL DEFAULT 0,
  notes                    text,
  is_deleted               boolean NOT NULL DEFAULT false,
  created_at               timestamptz NOT NULL,
  updated_at               timestamptz NOT NULL,
  resident_name_at_checkin text,
  CONSTRAINT visits_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.renewer_kit_items (
  sync_id         text    NOT NULL,
  product_sync_id text    NOT NULL,
  is_deleted      boolean NOT NULL DEFAULT false,
  updated_at      timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT renewer_kit_items_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.renewer_kit_deliveries (
  sync_id          text NOT NULL,
  resident_sync_id text NOT NULL,
  resident_name    text NOT NULL,
  room_number      text NOT NULL,
  delivered_by     text NOT NULL,
  delivered_at     timestamptz NOT NULL DEFAULT now(),
  notes            text,
  is_deleted       boolean NOT NULL DEFAULT false,
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT renewer_kit_deliveries_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.renewer_kit_delivery_items (
  sync_id              text    NOT NULL,
  delivery_sync_id     text    NOT NULL,
  product_sync_id      text    NOT NULL,
  product_name         text    NOT NULL,
  size_variant_sync_id text    NOT NULL,
  size                 text    NOT NULL,
  is_deleted           boolean NOT NULL DEFAULT false,
  updated_at           timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT renewer_kit_delivery_items_pkey PRIMARY KEY (sync_id)
);

CREATE TABLE IF NOT EXISTS public.reimbursements (
  sync_id      text    NOT NULL,
  resident_name text   NOT NULL,
  room_number  text    NOT NULL,
  amount       numeric NOT NULL,
  reason       text,
  is_paid      boolean NOT NULL DEFAULT false,
  is_deleted   boolean NOT NULL DEFAULT false,
  created_at   timestamptz NOT NULL DEFAULT now(),
  updated_at   timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT reimbursements_pkey PRIMARY KEY (sync_id)
);


-- ── 2. ENABLE ROW LEVEL SECURITY ───────────────────────────

ALTER TABLE public.products                   ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.size_variants              ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.stock_movements            ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.general_items              ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.general_item_loans         ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.residents                  ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.reservations               ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.deliveries                 ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.distribution_campaigns     ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.distribution_records       ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.rooms                      ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.bis_loans                  ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.pending_registrations      ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.audit_logs                 ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.visits                     ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.renewer_kit_items          ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.renewer_kit_deliveries     ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.renewer_kit_delivery_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.reimbursements             ENABLE ROW LEVEL SECURITY;


-- ── 3. POLICIES (anon = full access) ───────────────────────
-- The app uses the anon key without user authentication,
-- so all operations must be permitted for the anon role.

CREATE POLICY IF NOT EXISTS "anon_all" ON public.products
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.size_variants
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.stock_movements
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.general_items
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.general_item_loans
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.residents
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.reservations
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.deliveries
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.distribution_campaigns
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.distribution_records
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.rooms
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.bis_loans
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.pending_registrations
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.audit_logs
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.visits
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.renewer_kit_items
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.renewer_kit_deliveries
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.renewer_kit_delivery_items
  FOR ALL TO anon USING (true) WITH CHECK (true);

CREATE POLICY IF NOT EXISTS "anon_all" ON public.reimbursements
  FOR ALL TO anon USING (true) WITH CHECK (true);


-- ── 4. FUNCTIONS ───────────────────────────────────────────

-- Called by VisitService.GetOvernightTotalsAsync.
-- Counts completed-visit overnights per room since each resident's moved_in_at,
-- which gives the all-time overnight total for the current occupant.
--
-- Input:  room_cutoffs jsonb
--   { "101": {"resident_name": "...", "moved_in_at": "2025-01-01T00:00:00Z"}, ... }
-- Output: TABLE(room_number text, total integer)
CREATE OR REPLACE FUNCTION public.get_overnight_totals(room_cutoffs jsonb)
RETURNS TABLE(room_number text, total integer)
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
    SELECT
        rc.room                                 AS room_number,
        COALESCE(SUM(v.overnights), 0)::integer AS total
    FROM jsonb_each(room_cutoffs) AS rc(room, params)
    LEFT JOIN public.visits v
           ON v.room_number    = rc.room
          AND v.is_deleted     = false
          AND v.checked_out_at IS NOT NULL
          AND v.checked_in_at  >= (rc.params->>'moved_in_at')::timestamptz
    GROUP BY rc.room;
$$;

GRANT EXECUTE ON FUNCTION public.get_overnight_totals(jsonb) TO anon;


-- ── 5. REALTIME PUBLICATION ────────────────────────────────
-- Must match WatchedTables in SupabaseRealtimeService.cs.
-- Run each ALTER separately if any table is already in the publication.

ALTER PUBLICATION supabase_realtime ADD TABLE public.reservations;
ALTER PUBLICATION supabase_realtime ADD TABLE public.deliveries;
ALTER PUBLICATION supabase_realtime ADD TABLE public.general_item_loans;
ALTER PUBLICATION supabase_realtime ADD TABLE public.general_items;
ALTER PUBLICATION supabase_realtime ADD TABLE public.products;
ALTER PUBLICATION supabase_realtime ADD TABLE public.size_variants;
ALTER PUBLICATION supabase_realtime ADD TABLE public.residents;
