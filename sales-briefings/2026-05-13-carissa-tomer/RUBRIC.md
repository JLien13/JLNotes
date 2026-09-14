# Rubric — How This Briefing Was Built

## Goal
A self-contained packet a CorVascular rep can act on without further prep
from the sales manager.

## Inputs
1. Territory config: `assets/territories/<rep>.json` (states / accounts /
   domains the rep owns).
2. CRM (a1i-dev MCP, tenant=corvascular): orgs, contacts, quotes, orders,
   tickets, calls, notes, email_activity, reviews, trade_show table.
3. Government bids: `BIDS/seen_bids.json`, filtered by territory state via
   VISN→state map + title text scan.
4. Competitive sweep: per-competitor `Update-Log.md` files, last 30 days,
   CRITICAL/HIGH severities only.
5. Sister-skill references for product/pricing/ROI/competitor copy
   (`corvascular-lead-scraper`).

## Output structure
- `00-rep-brief.md` — 5-minute morning read.
- `01-todays-call-list.md` — ranked dial-down list with talking points.
- `02-stale-quotes/` — `.eml` drafts via the `compose-email` skill.
- `03-followups/` — `.eml` drafts for post-call / post-order touches.
- `04-prospect-shortlist.md` — top new prospects.
- `05-bid-radar.md` — territory bids with recommended action.
- `06-competitive-watch.md` — competitive items intersecting install base.
- `07-tradeshow-leads.md` — uncontacted recent show leads.
- `data.json` — full source data; future runs diff against this.

## Scoring
- HOT: open quote ≥ 3d old, callback scheduled, RMA quote sent, expiring
  loaner, fresh tradeshow lead.
- WARM: past customer >180d quiet, competitor named in `systems`/notes
  with a fresh CRITICAL/HIGH sweep finding, recent email opener.
- COLD: top-N by total_spent, surfaced sparingly.

## Hard guardrails
- a1i-dev MCP only (never a1i-crm).
- No DB writes (no `apply_migration`, no `execute_sql` writes).
- All emails are `.eml` drafts via `compose-email`. Rep reviews & sends.

## Sources skipped (deliberately)
- `IAC_MIDWEST_VASCULAR_LABS.csv` — header only; scraper hotspot blocker.
- CorVascular website "Market Intel" page — not present in source.
- `/corvascular-gov-bids` skill invocation — read on-disk artifacts only.
- `organizations.sales_rep_id` / `account_manager_id` — both 0/1454.

## Run log

## Run 2026-05-13 — Carissa Tomer (onboarding day 1)

**Territory:** Western/Central FL, area codes 352, 727, 813, 863, 941, 239, 321, 407
**Rep CRM record:** `48a0f370-ccd9-4ab6-9406-c4aad1fa4a79` — Carissa Tomer, RVT, carissa@ultrasoundenergy.com (consulting email; she is a CorV contractor, not a `@corvascular.com` user). No A1i auth.users record. Phone 941-302-0663 (her own number is *in* her territory).

**Pulled:**
- Group 1 ACTIVE practices: **139** (CSV)
- Group 1 hospitals top Tier A/B (REPLACEMENT + UPGRADE): **23** (CSV) — total in territory: 90
- Group 2 NOT_TESTING practices top 500: **500** (CSV) — total in territory: 6,495
- CRM orgs with phone in territory area codes: **13** (CSV)
- Historical FL orders (all-time): **19 orders, $77,073.91 total** — most concentrated 2014-2017, plus 2 recent (Vascular Surgery Assoc FL, Tallahassee/Panama City, 2024). None in Carissa's central FL area codes recent.
- Recent calls (60d) in territory: **1** (Megalabs USA, Miami — outside Carissa's area codes; not actionable)
- Open quote_requests in FL: **0**
- Open support_tickets in FL: **0**
- Tradeshow leads with FL area-code phone: **0**
- Territory bids in `BIDS/seen_bids.json`: **0**
- Competitive sweep CRITICAL/HIGH last 30d: **0**

**Top signal:** Bradenton Cardiology Center — prior CorV customer ($31,690 in 2014-2015), no activity in 11 years, 17 minutes from Carissa's home base. Prime reactivation play.

**Skipped this run (and why):**
- Full Group 2 NOT_TESTING universe beyond top 500 — too large for a single CSV (6,495 rows). Reachable via the A1i Market Intelligence page filter.
- GREENFIELD hospitals CSV — same reason; available via MI page.
- `.eml` drafts (stale-quote + follow-up) — no open quotes or recent customer-side interactions in territory to nudge. Day-1 focus is phone-dialing, not email follow-up.

**Schema corrections discovered during this run** (worth pushing into `references/data-sources.md`):
- `corvascular.contacts` has `first_name`+`last_name`, NOT `full_name`. Same for `trade_show`.
- `corvascular.organizations` does NOT have `customer_status` — uses `organization_status`. No `total_spent` column on org either; aggregate from `customer_orders` instead.
- Orders table is `customer_orders`, NOT `orders`.
- `quote_requests` has `total_price`, not `total_amount`; address is `address_city`/`address_state` (not joined to org by default).
- `phone_calls` has `call_type` + `call_purpose` (no `direction`); use `call_notes`, not `notes`.
- `trade_show.show_date` is **text**, not timestamp — can't compare with `> NOW() - INTERVAL`. Cast or use string comparison.

**Architectural note for next iteration:** the bulk of this rep's value comes from the Market Intelligence snapshot, not the CRM. As more reps onboard, formalize `corvascular.sales_territories` and wire territory selection into the MI page directly. Then this skill reads territory from the table (not from `assets/territories/*.json`) and the manager's per-rep onboarding becomes "add a row, run briefing."

**For next iteration:**
- Auto-pair MI practices to CRM orgs by NPI to surface "already in CRM" badges in the call list.
- Build territories table + MI page territory selector (rep-per-month scale prep).
- Add per-call logging template to `01-todays-call-list.md` so Carissa drops result codes directly into a follow-up file.
- When the trade_show table gets FL leads, surface them automatically.
