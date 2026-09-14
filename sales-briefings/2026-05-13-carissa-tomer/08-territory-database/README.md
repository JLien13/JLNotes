# Territory Database — Western / Central Florida

**Generated:** 2026-05-13
**Rep:** Carissa Tomer
**Filter:** State = FL AND phone area code IN (352, 727, 813, 863, 941, 239, 321, 407)
**Data source:** A1i Market Intelligence snapshot (`mi_practice_opportunities` + `mi_opportunities`), CRM (`organizations` + `customer_orders`)

## Files

### `group-1-active-billers-practices.csv` (139 rows)
Every practice in your area codes that bills CPT 93922, 93923, or 93924 in the latest Medicare utilization data.

**Columns:** npi, provider_name, specialty, street, city, state, zip, phone, svc_93922, svc_93923, svc_93924, total_services, total_beneficiaries, est_annual_revenue, opportunity_score, priority_tier

**Use:** Replacement targets. They already have a workflow — your job is to displace their current system.

### `group-1-active-billers-hospitals.csv` (23 rows)
Top Tier A/B hospitals in your area codes where the ZIP has vascular billing happening (REPLACEMENT or UPGRADE classification). Full 90-hospital list is accessible via the A1i Market Intelligence page when you're ready to widen.

**Columns:** hospital_name, city, zip, phone, bed_cnt, opportunity_type, opportunity_score, priority_tier

### `group-2-not-testing-practices-top500.csv` (500 rows)
Top 500 (by opportunity score) practices in your area codes that match the Group 1 specialty profile but are NOT billing the codes. These are greenfield — they have the patient population but no current workflow.

Full universe is ~6,495 rows. View/filter all of them in the A1i Market Intelligence page.

**Columns:** npi, provider_name, specialty, street, city, state, zip, phone, opportunity_score, priority_tier, providers_in_zip, services_in_zip

### `crm-orgs-in-territory.csv` (13 rows)
Organizations already in CorVascular's CRM with phones in your area codes. Includes prior-customer status, historical order totals, and last order date.

**Columns:** id, facility_name, address, city, zip, phone, status, historical_orders, historical_total, last_order_date, notes

## How to use the universe

| Day | Source | What you do |
|---|---|---|
| 1 | `01-todays-call-list.md` | 12 dials from Group 1 practices |
| 2-5 | Group 1 practices (rows 13-50) | Continue dialing through top scored |
| 6-10 | CRM orgs (especially the 2 prior customers) + Group 1 hospitals top 10 | In-person visits where possible |
| 11+ | Group 2 top 500 — start with vascular surgery / cardiology in your closest ZIPs | First-touch outreach |

When you've burned through these, expand via the A1i Market Intelligence page (ask manager for URL).

## Refresh cadence

This database is generated from the MI snapshot, which itself refreshes from CMS data (1-year lag — current data is 2023 Medicare). The manager will refresh your territory CSVs at the start of each week.
