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

## Run 2026-05-07 09:00
- **Counts pulled:** orgs=60, open_quotes=0, recent_calls=21,
  recent_orders=2 (non-zero), open_sales_tickets=4, stale_customers=8,
  tradeshow_leads=0, territory_bids=3, competitive_findings=7.
- **Top signal:** HSHS St. Joseph's-Breese (IL) ticket #162653 — F14
  trade-in conversation surfaced on the 04/03/2026 call. Drafted the V8
  trade-in email; manager should consider that the day's focus.
- **Skipped this run:**
  - `iac_midwest_vascular_labs_csv` — file is header-only; scraper hotspot
    blocker open.
  - `corvascular_market_intel_page` — no such page in the public website
    source as of 2026-05-07.
  - `tradeshow_leads_180d` — `corvascular.trade_show` table empty.
  - `open_quote_requests_in_territory` — no quote_requests in
    `new`/`contacted` status; conversion seems to happen quickly or routes
    through a different table.
- **For the next iteration:**
  - Wire `customer_orders` more deeply — the recent-orders pull found the
    AMI shipment that drove a HOT follow-up. A 30-day standing pull for
    shipments in territory should be its own ranked input.
  - Add a "TRIMEDX-assigned facilities" filter — many tickets in territory
    have a TRIMEDX biomed contact, and treating TRIMEDX as a domain pivot
    pulls in cross-territory accounts the rep should still know about.
  - Newsletter / bulk-email opener data: pull `email_activity` events with
    `status='opened'` per territory contact in the last 60 days to elevate
    WARM scores. Today it's not wired in yet.
  - Push competitive Update-Log extractor to surface specific *territory
    accounts* by grepping the install-base `systems` jsonb for competitor
    product names (e.g. Newman ABI-Q, Parks Flo-Lab) and join those back
    to the orgs table — today the matching is by hand.
  - Auto-discover the rep's email signature / availability windows so the
    .eml drafts can suggest specific calendar slots ("Tue 10:30 or Thu
    2pm") instead of "two windows that work."
