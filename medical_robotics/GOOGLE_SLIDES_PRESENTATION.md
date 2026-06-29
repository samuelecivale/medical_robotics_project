# Multi-RCM Kinematic Control — Visual Redesign Specification
### Target: Google Slides · 16:9 · ~10 minutes · 17 slides

---

## Design system

**Palette (robotics / medical-tech blue-teal)**

| Role | Color | Hex |
|---|---|---|
| Primary background (dark slides) | Deep navy | `#0A2540` |
| Secondary panel | Deep blue | `#123C69` |
| Accent / highlight | Teal | `#17C3B2` |
| Accent light | Light teal | `#5EEAD4` |
| Warning / honesty callouts | Amber | `#F4A259` |
| Light background (white slides) | Off-white | `#F7FAFC` |
| Body text on light | Ink | `#0A2540` |
| Muted secondary text | Slate | `#5B6B7F` |
| Body text on dark | Cloud | `#DCE6F2` |

**Typography**
- Titles: **Montserrat**, Bold, 36–44 pt, all key titles left-aligned, one line if possible.
- Kicker labels (small caps section tag above title): Montserrat SemiBold, 12 pt, letter-spaced, teal.
- Body / bullets: **Roboto**, Regular, 16–18 pt, max 4 lines per slide.
- Numbers / stats: Montserrat Bold, 48–72 pt.
- Captions under figures: Roboto Italic, 12 pt, slate.

**Layout rules**
- Consistent margin: 0.6 in on all sides.
- Every slide has exactly **one dominant visual** and **one idea**.
- Thin 4 pt teal accent rule under every slide title.
- Footer on every slide: slide number (right) + section tag (left), 10 pt, slate, low-contrast.
- Icons: simple monochrome glyphs (✓ ✕ → ● ◆ ▣) inside a solid teal or navy circle — not stock clip-art, not colorful emoji, to keep the IEEE-conference feel.

**Animations (Google Slides–compatible, use sparingly)**
- "Fade in" for titles.
- "Fly in from bottom" for one bullet group / one figure per slide, *by paragraph*, not by letter.
- "Fade in" for sequential diagram steps (e.g., 3-phase timeline appears step-by-step).
- No spinning / bouncing / letter-by-letter effects — keep it conference-grade.

**Image provenance note (for transparency):**
The Unity screenshot used in this deck (`PresentationAssets/unity_scene_wide.jpg`) is a frame extracted with `ffmpeg` from `Recordings/Movie_002.mp4`. It is from an earlier development build, but the robot, needle, and entry/target marker visualization it shows are representative of the current scene.

---

## Slide 1 — Title

- **Layout:** Full-bleed dark navy background, left-aligned text block vertically centered, thin teal rule, small footer.
- **Main visual:** None (typography-led); a faint, large, low-opacity outline of a 6-DOF arm silhouette in the bottom-right corner (decorative, built from simple shapes, not a photo).
- **Text:**
  - Kicker: "MEDICAL ROBOTICS — MASTER'S PROJECT"
  - Title: "Multi-RCM Kinematic Control for a ROSA-like Surgical Robot"
  - Subtitle: "A Unity simulation of double Remote-Center-of-Motion control"
  - Footer: name · date
- **Speaker notes:** Good morning. Today I'll present a Unity-based simulation of a ROSA-like surgical robot implementing double Remote-Center-of-Motion control, inspired by Aghakhani et al., ICRA 2013. I'll walk through the clinical motivation, the controller, the simulation, and the validation evidence — all from data I logged and analyzed myself.
- **Colors:** Background `#0A2540`; title `#F7FAFC`; kicker/subtitle `#17C3B2`.
- **Icons:** None.
- **Animation:** Title fades in (0.4s), subtitle fades in 0.3s after.
- **Time:** 20 s

---

## Slide 2 — The Clinical Problem

- **Layout:** Split 40/60. Left: one bold statement + tiny supporting line. Right: minimal vector diagram (skull arc, entry dot, dashed insertion line, target dot) built from simple shapes — no stock anatomy art.
- **Main visual:** Custom geometric diagram: a light gray arc (skull contour), a teal dot labeled "Entry", a dashed line, a navy dot labeled "Target".
- **Text:**
  - Title: "One Entry Point. Zero Lateral Force."
  - Body (left, 2 lines max): "Stereotactic neurosurgery inserts a rigid tool through a single skull opening toward a deep target. Any sideways force at that opening risks tissue damage."
- **Speaker notes:** In minimally invasive neurosurgery — biopsy, electrode placement — the surgeon plans one entry point and one deep target from imaging. The instrument must pass through that entry point without levering against the skull. This single constraint is the whole motivation for what follows.
- **Colors:** Background `#F7FAFC`; diagram arc `#CBD5E1`; entry dot `#17C3B2`; target dot `#0A2540`; title `#0A2540`.
- **Icons:** None (diagram replaces icon).
- **Animation:** Diagram elements fade in in sequence: arc → entry dot → line → target dot.
- **Time:** 35 s

---

## Slide 3 — The Concept: Remote Center of Motion

- **Layout:** Centered, diagram-first. Top half: a pivoting-needle diagram (needle line, pivot dot at ~30% length, two faint dashed positions of the needle rotated around the pivot). Bottom half: one equation card.
- **Main visual:** Custom pivot diagram + equation displayed inside a rounded card (deep blue `#123C69` background, light teal monospace text).
- **Text:**
  - Title: "A Pivot Point That Isn't Physically There"
  - Equation card: `p_RCM(q, λ) = p_base(q) + λ·L·z(q)`
  - One-line caption: "λ slides the RCM point along the tool shaft as insertion depth changes."
- **Speaker notes:** An RCM is a virtual pivot — the tool axis always passes through it, even though no mechanical hinge exists there. Here it's realized purely through control: lambda is a scalar that slides a point along the needle shaft, and the controller forces that point to coincide with the entry marker.
- **Colors:** Background `#F7FAFC`; equation card `#123C69` with text `#5EEAD4`; pivot diagram lines `#0A2540`, pivot dot `#17C3B2`.
- **Icons:** None.
- **Animation:** Pivot diagram draws first (fade), equation card flies in from bottom second.
- **Time:** 40 s

---

## Slide 4 — Why It Matters: With vs. Without RCM

- **Layout:** Two equal columns, card-style, divided by a thin vertical teal rule.
- **Main visual:** Two icon-led comparison cards.
- **Text:**
  - Title: "Constraining the Pivot Changes Everything"
  - Left card — icon ✕ in amber circle: "Without RCM" / "Shaft levers against the entry point → lateral tissue load"
  - Right card — icon ✓ in teal circle: "With RCM" / "Tip reaches target while the shaft pivots cleanly through entry"
- **Speaker notes:** Without an RCM constraint, nothing stops the shaft from pushing sideways against the skull as the arm reconfigures. With the RCM formulated as an extra task stacked alongside the tip task, the robot can reach the target and keep the pivot locked at the entry simultaneously — this project adds a twist: in some modes the "effective" RCM is relocated to the target itself, which is the double-RCM idea in the title.
- **Colors:** Left card bg `#FDEEDD`, icon circle `#F4A259`; right card bg `#E6FBF8`, icon circle `#17C3B2`; title `#0A2540`.
- **Icons:** ✕ (amber circle), ✓ (teal circle).
- **Animation:** Left card flies in from left, right card flies in from right (slight stagger).
- **Time:** 35 s

---

## Slide 5 — Project at a Glance

- **Layout:** Full-width dashboard of 4 stat tiles in a single row.
- **Main visual:** 4 rounded rectangle tiles, large number + short label each.
- **Text:**
  - Title: "Project at a Glance"
  - Tile 1: "6" / "DOF procedural arm"
  - Tile 2: "4" / "Task modes"
  - Tile 3: "1" / "Controller, built from scratch"
  - Tile 4: "9 / 9" / "Validation checks passed"
- **Speaker notes:** Before going into detail, here's the project in four numbers: a procedural six-degree-of-freedom ROSA-like arm, four selectable task modes, one hand-written damped-least-squares controller — no external IK library — and nine out of nine automatic validation checks passed on the logged session, which I'll detail later.
- **Colors:** Background `#0A2540`; tiles `#123C69`; numbers `#5EEAD4`; labels `#DCE6F2`.
- **Icons:** None — numbers are the visual.
- **Animation:** Tiles fade/fly in left-to-right, one after another (fast, 0.15s stagger).
- **Time:** 25 s

---

## Slide 6 — Four Tasks, One Controller

- **Layout:** 2×2 grid of cards, equal size, consistent padding.
- **Main visual:** Four cards, each with a glyph icon, task number badge, and a 1-line description.
- **Text:**
  - Title: "Four Tasks, One Controller"
  - Card 1 — ● icon: "Task 1 — Entry-RCM + Tip Target"
  - Card 2 — ◆ icon: "Task 2 — Target-RCM + Entry-Side Cone"
  - Card 3 — ▣ icon: "Task 3 — Insertion Sequence (main task)"
  - Card 4 — ◐ icon: "Task 4 — Entry-RCM + Tip Cone Around Target"
- **Speaker notes:** All four modes are implemented in the current controller — I verified this directly in the code, not just in old plots. Task 1 and Task 3 both anchor the RCM at the entry; Task 2 and Task 4 explore a cone of motion around a pivot, with the pivot location swapped between the two. Task 3 is the realistic surgical sequence and the one I'll detail next.
- **Colors:** Background `#F7FAFC`; cards `#FFFFFF` with `#123C69` border 1pt; icon circles `#17C3B2`; card titles `#0A2540`.
- **Icons:** ● ◆ ▣ ◐ in teal circles.
- **Animation:** Grid cells fade in in reading order (top-left → bottom-right), 0.1s stagger.
- **Time:** 45 s

---

## Slide 7 — Inside Task 3: The Insertion Sequence

- **Layout:** Horizontal 3-step timeline across the top half (boxes connected by arrows); one supporting plot, cropped tight, in the bottom half.
- **Main visual:** Timeline diagram: `ApproachEntry → PierceEntry → InsertToTarget`, each step a rounded box with a 1-line description; arrows between them. Below: `RCM_analysis_clean/03b_T3_tip_line_distance.png` (cropped/scaled), captioned.
- **Text:**
  - Title: "Inside Task 3: The Insertion Sequence"
  - Step 1: "Align needle axis, approach outside the trocar"
  - Step 2: "Tip travels to the entry point (RCM not yet active)"
  - Step 3: "RCM locked at entry — tip advances to target"
  - Figure caption: "Tip-to-corridor distance collapses to ~0 mm once insertion begins."
- **Speaker notes:** Task 3 is a three-phase state machine mirroring the clinical sequence: align and approach, pierce the trocar, then insert with the RCM constraint active. The plot underneath shows the tip's distance from the planned straight entry-target line: it starts near 400 mm while the arm is still approaching, then collapses to essentially zero once the tip reaches the trocar — meaning the needle travels along the planned corridor, not an arbitrary path.
- **Colors:** Background `#F7FAFC`; timeline boxes `#123C69` text `#F7FAFC`; arrows `#17C3B2`; figure panel border `#CBD5E1`.
- **Icons:** → between boxes.
- **Animation:** Timeline boxes appear left-to-right with arrows drawing between them; figure fades in last.
- **Time:** 45 s

---

## Slide 8 — Controller Architecture

- **Layout:** Horizontal pipeline diagram, 4 boxes connected by arrows, full width, centered vertically.
- **Main visual:** Block diagram: `Stacked point tasks` → `Damped Least Squares` → `Null-space correction` → `Joint + λ velocities`.
- **Text:**
  - Title: "Controller Architecture"
  - Box 1: "Tip / cone / RCM tasks stacked into one Jacobian"
  - Box 2: "Damped least-squares solve (custom Gauss–Jordan)"
  - Box 3: "Null-space: joint limits, λ target, secondary objectives"
  - Box 4: "Saturated joint + λ̇ velocities → integrated"
  - Small caption under diagram: "Jacobians computed by numerical finite differencing."
- **Speaker notes:** Every control step stacks the active point-tasks — tip, cone, RCM — into one Jacobian and solves it with a damped least-squares pseudoinverse I implemented from scratch with a Gauss-Jordan solver. A null-space term is projected into the task Jacobian's kernel to bias the solution toward keeping joints centered and lambda near its target, without disturbing the primary tasks. Jacobians themselves are computed numerically, a deliberate simplicity trade-off that keeps the DH table easily replaceable.
- **Colors:** Background `#0A2540`; boxes `#123C69` border `#17C3B2`; box text `#DCE6F2`; arrows `#5EEAD4`.
- **Icons:** → connecting arrows only.
- **Animation:** Boxes and arrows appear left-to-right, one step per click.
- **Time:** 35 s

---

## Slide 9 — The Control Law, in Full

- **Layout:** 2×2 grid of equation cards, dark navy background (paired visually with Slide 8), monospace equation type, one integration line spanning the bottom.
- **Main visual:** Four equation cards, each a label + a compact mathematical expression rendered in monospace:
  1. **Stacked task Jacobian** — `[ẋ_task; ẋ_RCM] = [J_task 0; J_RCM] · [q̇; λ̇]`
  2. **Per-task error → velocity** — `ẏ_i = K_i · (x_des,i − x_i)`, saturated
  3. **Damped least-squares solve** — `[q̇; λ̇] = Jᵗ (J Jᵗ + μ²I)⁻¹ y`
  4. **Null-space secondary objective** — `+ (I − J⁺J) · w`, with `w`: joint limits, λ target
  - Bottom strip: `Integration (saturated): q_{k+1} = clamp(q_k + q̇·Δt)  ·  λ_{k+1} = clamp(λ_k + λ̇·Δt, 0.02, 0.995)`
- **Text:** Equation cards only — no prose bullets. This slide is intentionally the densest in the deck; everything else is built on top of it.
- **Speaker notes:** Here is the control law in full. The active point tasks — tip, cone, RCM — are stacked into one Jacobian, each weighted by a gain and a clamped error. The stacked system is solved with a damped least-squares pseudoinverse: q-dot and lambda-dot equal J-transpose times the inverse of J·J-transpose plus mu-squared identity, times the desired task velocity y. A null-space term is then projected into the kernel of J and added on top, to bias the solution toward keeping joints centered and lambda near a target value, without disturbing the primary tasks. Finally, both joint and lambda velocities are saturated before being integrated forward in time. Every piece of this — including the Gauss-Jordan linear solve underneath the pseudoinverse — is implemented from scratch in C#, with no external IK or optimization library.
- **Colors:** Background `#0A2540`; cards `#123C69`; equation text `#FFFFFF` (Consolas/monospace); card labels `#5EEAD4`; bottom strip `#DCE6F2` italic.
- **Icons:** None — the equations themselves are the visual.
- **Animation:** Cards fade in one at a time, reading order (top-left → bottom-right); bottom integration line appears last.
- **Time:** 40 s

---

## Slide 10 — Built in Unity

- **Layout:** Split 45/55. Left: three stacked labeled boxes (one per script). Right: full-bleed screenshot.
- **Main visual:** `PresentationAssets/unity_scene_wide.jpg` (right), three script boxes (left).
- **Text:**
  - Title: "Built in Unity — Three Scripts"
  - Box A: "Project4SceneBuilder.cs — builds the scene procedurally"
  - Box B: "ROSADoubleRCMController.cs — kinematics, control law, logging"
  - Box C: "FreeFlyCameraKeyboard.cs — keyboard fly-camera"
  - Caption under image: "Simulation view: procedural arm, needle, target marker, live HUD. (Captured from an earlier development build.)"
- **Speaker notes:** The entire scene is generated at runtime from a single script, rather than hand-built in the editor, which keeps the project self-contained. The controller script does everything robot-related: kinematics, the control law, the insertion state machine, the on-screen overlay, and CSV logging. A separate, independent script provides a keyboard fly-camera for free inspection during recording. The screenshot is from an earlier development build, but the robot, needle, and marker visualization are representative of the current scene too.
- **Colors:** Background `#F7FAFC`; script boxes `#123C69` text `#F7FAFC`; caption `#5B6B7F` italic.
- **Icons:** Small ▣ glyph before each script box label.
- **Animation:** Image fades in first (sets context), then boxes fly in from the left, one by one.
- **Time:** 35 s

---

## Slide 11 — From Simulation to Evidence

- **Layout:** Horizontal 3-node flow diagram, centered, generous whitespace.
- **Main visual:** `Unity controller (20 Hz CSV)` → `analyze_rcm_logs_clean.py` → `Plots + pass/fail tables`.
- **Text:**
  - Title: "From Simulation to Evidence"
  - Node 1: "Live CSV log — ~20 Hz, every relevant variable"
  - Node 2: "Python analyzer — one metric per plot"
  - Node 3: "Thresholded plots + `pass_fail_checks.csv`"
- **Speaker notes:** Validation here is data-driven. The controller logs every relevant quantity to CSV at about 20 hertz while running. A Python pipeline — the "clean" analyzer, written specifically to replace an earlier version that crammed every error into one unreadable chart — turns that log into one focused, thresholded plot per metric, plus summary tables. The plots in this deck are reused directly from that pipeline's output, not redrawn.
- **Colors:** Background `#F7FAFC`; nodes `#FFFFFF` border `#17C3B2`; node text `#0A2540`; arrows `#0A2540`.
- **Icons:** None — node shapes carry the meaning.
- **Animation:** Nodes and arrows appear left-to-right.
- **Time:** 30 s

---

## Slide 12 — Results: Everything Passes

- **Layout:** Top: one giant stat. Bottom: full-width figure.
- **Main visual:** `RCM_analysis_clean/99_validation_summary_normalized.png`, large, full width.
- **Text:**
  - Title: "Results: Everything Passes"
  - Giant stat: "9 / 9 checks passed"
  - One-line takeaway: "Tightest margin: entry-RCM error during insertion, ~90% of its 10 mm threshold."
- **Speaker notes:** This bar chart normalizes every automatic check against its own threshold. All nine pass. The tightest margins are the entry-RCM constraints during Task 3 insertion and Task 4 — both around ninety percent of their ten-millimetre allowance, worth noting as a margin rather than a failure. Task 1 is essentially perfect, under two percent of its threshold.
- **Colors:** Background `#0A2540`; giant stat `#5EEAD4`; figure framed in a white card `#F7FAFC` for contrast.
- **Icons:** None.
- **Animation:** Stat fades/scales in first, figure flies in from bottom second.
- **Time:** 40 s

---

## Slide 13 — Results: Safe Insertion (Task 3)

- **Layout:** Two figures side by side, equal size, shared caption row beneath.
- **Main visual:** `RCM_analysis_clean/02_T3_entry_rcm_error.png` (left), `RCM_analysis_clean/03b_T3_tip_line_distance.png` (right).
- **Text:**
  - Title: "Results: Safe Insertion (Task 3)"
  - Caption left: "Entry-RCM error → ~0 mm once insertion starts"
  - Caption right: "Tip stays on the planned entry–target line"
- **Speaker notes:** These two plots validate the core neurosurgical claim for the main task. The entry-RCM error is not enforced before the tip reaches the trocar — by design — then converges to essentially zero once true insertion begins. The tip-to-line-distance plot is the most intuitive for a clinical audience: after the approach phase, the needle tip travels almost exactly along the planned straight corridor from entry to target.
- **Colors:** Background `#F7FAFC`; figure frames `#CBD5E1`; captions `#5B6B7F`.
- **Icons:** None.
- **Animation:** Left figure flies in, then right figure.
- **Time:** 40 s

---

## Slide 14 — Results: Tracking Moving Targets (Tasks 2 & 4)

- **Layout:** Two figures side by side + one shared takeaway line beneath, smaller than Slide 13 to signal "secondary evidence."
- **Main visual:** `RCM_analysis_clean/04_T2_entry_cone_error.png` (left), `RCM_analysis_clean/07_T4_tip_cone_error.png` (right).
- **Text:**
  - Title: "Tracking Moving Targets (Tasks 2 & 4)"
  - Takeaway: "Bounded, oscillating tracking error — expected for a continuously rotating target, both well under threshold."
- **Speaker notes:** For the cone tasks, zero steady-state error isn't the right expectation, since the desired point is continuously rotating. What matters is that tracking stays bounded and below threshold throughout, which both plots confirm — with visibly less margin than the static Task 1/3 targets, consistent with tracking a moving point under a simultaneous RCM constraint being intrinsically harder.
- **Colors:** Background `#F7FAFC`; figure frames `#CBD5E1`; takeaway text `#0A2540`.
- **Icons:** None.
- **Animation:** Both figures fade in together (no stagger — this slide is faster-paced).
- **Time:** 35 s

---

## Slide 15 — Limitations

- **Layout:** Single column, compact icon-led list, muted palette (deliberately lower visual energy than results slides).
- **Main visual:** 5-row icon list.
- **Text:**
  - Title: "Limitations"
  - "◐  ROSA-*like*, not an official CAD model — plausible DH, not measured"
  - "◐  Needle = rigid cylinder, no tissue interaction"
  - "◐  No anatomical collision avoidance in the final build"
  - "◐  Purely kinematic — no dynamics, no force control"
  - "◐  Jacobians computed numerically, not symbolically"
- **Speaker notes:** It's important to state plainly what this project doesn't claim. The geometry is plausible, not an official ROSA model. There's no tissue interaction, no force control, and no active obstacle avoidance in the final build. The controller is purely kinematic, and Jacobians are numerical rather than closed-form. None of this invalidates the kinematic result — it bounds what can be claimed from it.
- **Colors:** Background `#F7FAFC`; icon circles `#5B6B7F` (muted slate, not teal — visual cue that this slide is "lower energy"/cautionary); text `#0A2540`.
- **Icons:** ◐ in slate circles.
- **Animation:** Simple sequential fade, no flying — matches restrained tone.
- **Time:** 25 s

---

## Slide 16 — What's Next

- **Layout:** Horizontal roadmap, 4 boxes connected by a single arrow line, left to right = near-term to long-term.
- **Main visual:** Roadmap diagram.
- **Text:**
  - Title: "What's Next"
  - Step 1: "Analytic Jacobian (replace finite differences)"
  - Step 2: "Re-introduce anatomy as a separate validation layer"
  - Step 3: "Real ROSA DH parameters, if available"
  - Step 4: "Randomized multi-trial validation"
- **Speaker notes:** The most natural next step is an analytic Jacobian, for speed and numerical robustness. Re-introducing anatomy should happen as an explicitly separate validation layer, not mixed back into the core RCM logic, exactly as the project's own documentation argues. Beyond that: real ROSA kinematic parameters if they become accessible, and validating over many randomized entry/target configurations instead of a single logged run, to get statistical rather than anecdotal evidence.
- **Colors:** Background `#0A2540`; boxes `#123C69`; arrow line `#17C3B2`; text `#DCE6F2`.
- **Icons:** Small numbered badges (1–4) in teal circles instead of generic icons.
- **Animation:** Roadmap line draws left-to-right, boxes pop in as the line reaches them.
- **Time:** 25 s

---

## Slide 17 — Conclusion

- **Layout:** Centered closing statement, full-bleed dark navy, mirrors Slide 1 for symmetry.
- **Main visual:** None — typography-led, plus the "9/9" stat repeated small as a closing anchor.
- **Text:**
  - Title: "A Validated Double-RCM Controller, Honestly Scoped"
  - Body: "Four tasks. One controller built from scratch. Nine of nine checks passed. Anatomy deliberately left out to keep the focus on the RCM formulation."
  - Closing line: "Thank you — questions welcome."
- **Speaker notes:** To close: this project turns the double-RCM formulation from Aghakhani et al. into a working, validated Unity simulation — four task modes, a from-scratch damped least-squares controller, and a full logging-and-validation pipeline. The evidence shows the RCM constraint held within the defined thresholds across every tested task. The scope was kept deliberately narrow — kinematics, not anatomy or dynamics — and stated as such throughout. Thank you, I'm happy to take questions.
- **Colors:** Background `#0A2540`; title `#F7FAFC`; body `#DCE6F2`; closing line `#17C3B2`.
- **Icons:** None.
- **Animation:** Fade in, matching Slide 1.
- **Time:** 25 s

---

## Total estimated speaking time: ~9 min 45 s (plus natural pauses ≈ 10 min)
