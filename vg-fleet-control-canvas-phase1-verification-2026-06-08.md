# VG-Fleet "Control Canvas" — Phase 1 Verification Record

**Status: PASS**
**Date conducted:** 2026-06-08
**Conducted by:** Jacob Lien (with Claude Code, paired)
**Record location:** kept OUTSIDE the source repo intentionally (informal record;
to be filed into the QMS / formal V&V later).

> Minimal verification record for the Control Canvas Phase 1 build. Captures what
> was tested, how, and the result, so there is a dated artifact for regulatory
> purposes. Not yet a controlled V&V document — placeholder to be formalized.

---

## Item under test

A new VG-Fleet **Central** feature: a per-rig **Control** tab that reconstructs
the VasoGuard screen from the Windows UIA control tree (`vg_ui_snapshot`) as a
live, to-scale, clickable vector canvas, with patient identifiers (PHI) redacted
on the rig before transmission.

Design reference: `vg-fleet/docs/plans/2026-06-08-control-canvas-design.md`.

### Provenance (configuration under test)
- Repo: `github.com/CorVascular/vg-fleet`
- Branch: `feature/control-canvas`
- Commits:
  - `efe82c6` — docs: Control Canvas design (Phase 1 plan)
  - `15381ae` — agent: vg_ui_snapshot + pure snapshot core (geometry, PHI redaction, frame assembly)
  - `8452e09` — central: Control Canvas tab (clickable reconstructed VG screen, source-side PHI redaction, gated reveal)
  - HEAD at test time: `8452e0906a9db913a8a47e3aaddb674a84561d19`

### Test environment
- OS: Windows 11 Home 10.0.26200
- Python 3.13.2
- pytest 9.0.3
- PySide6 6.11.1
- vgfleet 0.3.9
- Integration observation run against a live VasoGuard instance on the test host.

---

## Method

- **Test-Driven Development (test-first):** every new function/handler/widget had
  a failing test written and observed to fail before implementation, then driven
  to pass (red → green).
- **Automated unit + integration tests** via pytest. Qt widgets exercised
  headless (`QT_QPA_PLATFORM=offscreen`). Pure logic (geometry, PHI, frame
  assembly) tested with no Qt and no rig.
- **Live integration observation:** the `vg_ui_snapshot` agent handler was run
  against the running VasoGuard on the test host and the resulting frame
  inspected for correctness (geometry, control classification, PHI handling).

---

## Results — automated tests

**Control-Canvas-specific suite: 44 passed, 0 failed.**
**Full VG-Fleet regression suite: 320 passed, 0 failed.**

### `tests/test_ui_snapshot.py` (pure core — 16, all PASS)
Geometry transform (window-relative placement, top-right stays top-right, exact
inversion of the center convention); PHI classifier (patient-name echo,
patient-field auto-id, no over-match of facility/buttons/labels); redaction
(masks when redacted, fail-safe default to redacted, preserves only when
explicitly revealed, leaves non-PHI untouched); frame assembly (window-relative
controls, interactive/editable flags, **patient_present reported without the
name appearing anywhere in the serialized frame**, reveal only when revealed,
unknown mode fails safe to redacted, richer non-PHI state passthrough).

### `tests/test_control_canvas.py` (widget — 21, all PASS)
Render places one item per control at exact window-relative scene coordinates;
top-right control renders top-right; scene matches window size; ribbon reflects
screen id + patient presence; re-render replaces (not appends); click→primitive
routing (prefers auto-id, falls back to name, none for non-interactive, edit
uses auto-id, **never clicks a masked PHI name**, PHI-with-auto-id still acts by
id); inspect mode suppresses activation; PHI mode defaults redacted; reveal
blocked when disabled for Central; reveal denied when 2FA fails; reveal flips
mode after gate; reveal resets when patient leaves; switching peer resets reveal;
reveal button shown only with a patient.

### `tests/test_peer_panel.py` (integration — 2, all PASS)
Control tab is first and default (`Control, Live, Telemetry, Actions, Audit`);
selected peer propagates to the Control view.

### `tests/test_handlers_vg.py` (agent handler — 5, all PASS)
Includes new `test_vg_ui_snapshot_returns_well_formed_frame_or_clean_error`
(handler always returns a Response, redact-by-default frame shape when VG is
reachable, clean error otherwise); existing vg-handler tests still pass.

---

## Results — live integration observation

`vg_ui_snapshot` run against the live VasoGuard, frame inspected:

- Window: 2580 × 1620, title `MainView`, not minimized.
- Screen (via brain.describe_state): `PerformExam`, confidence 1.0, facility
  read, modal stack empty.
- Controls: 420 total, 162 interactive, **1 PHI detected and redacted**,
  `phi_mode = redacted`.
- **Geometry confirmed on real data:** VasoGuard's `ExitButton` reconstructed at
  window-relative `[2428, 50, 102, 75]` in a 2580-wide window → top-right corner,
  matching VG's actual layout. The full top-right toolbar (Settings, Keyboard,
  Capture, Record, Help, Minimize, Exit) reconstructed in order at y=50.
- Whole-screen frame size: ~68 KB (vs. a multi-MB full-screen PNG).

---

## Safety-relevant behaviors verified

- PHI is **redacted by default** and at the source (agent), before transmission.
- The serialized frame **never contains the raw patient name** when redacted
  (asserted by test).
- Revealing PHI requires the **2FA gate** and is **audited** (one-shot audited
  fetch), with a **Central-wide hard-off** in Settings.
- A reveal **auto-resets** to redacted when the patient context leaves the screen
  or the operator switches rigs (no carry-over between patients/rigs).
- An unknown/garbage `phi_mode` **fails safe to redacted**.
- A redacted PHI control is **never clicked by its (masked) name** — only by
  auto-id, so no masked value is ever sent as a click target.

---

## Known limitations / not yet verified

- **Visual GUI render not yet eyeballed:** automated tests verify the data path
  and widget geometry headlessly; the canvas has not yet been visually observed
  painting a live frame inside the running Central GUI. (Next manual check.)
- PHI classifier auto-id token list is an initial set; to be hardened against
  VasoGuard's confirmed automation ids on a rig.
- Interactive/editable control-type sets are an initial mapping; tune on a rig.
- Phase 2 (brain-backed "Go to…" navigator, macro palette, live change-diff) not
  yet built.

## Conclusion

Phase 1 of the Control Canvas meets its design intent. All 44 feature tests and
the full 320-test regression suite pass. Live integration confirms faithful
geometry reconstruction and source-side PHI redaction. **Result: PASS**, pending
the manual visual GUI check and Phase 2.
