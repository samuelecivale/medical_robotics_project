# Risultati pronti per le slide

## Messaggio principale
- Il controller implementa più task cinematiche: Entry-RCM, Target-RCM con cono, sequenza di inserzione e tip-cone con Entry-RCM.
- I grafici generati mostrano tracking degli errori, stabilità del fulcro RCM e sicurezza/corridoio durante l'inserzione.

## Numeri da riportare
- Task 1 — max errore RCM su entry: 0.0 mm; max errore tip-target: 0.2 mm.
- Task 2 — max errore entry-cone: 25.5 mm; max errore tip-target: 14.3 mm.
- Task 3 — max errore RCM durante inserzione: 8.9 mm; max violazione skull: 0.0 mm; errore finale sul target: 0.3 mm.
- Task 4 — max errore Entry-RCM: 9.0 mm; max errore tip-cone: 27.7 mm.

## Figure consigliate
- 00_task_timeline.png: sequenza dei task eseguiti.
- 01_tracking_errors.png: confronto complessivo degli errori.
- 02_insertion_detail.png: sicurezza e allineamento durante l'inserzione.
- 03_task2_cone.png e 04_task4_cone.png: qualità del moto conico.

## Check automatici
- PASS: T1 entry RCM stability — max entry_rcm_error_mm = 0.0 mm, soglia 10.0 mm.
- PASS: T1 tip reaches target — max tip_target_error_mm = 0.2 mm, soglia 25.0 mm.
- PASS: T2 cone tracking — max task2_entry_cone_error_mm = 25.5 mm, soglia 50.0 mm.
- PASS: T2 tip fixed at target — max tip_target_error_mm = 14.3 mm, soglia 25.0 mm.
- PASS: T3 insertion entry RCM — max entry_rcm_error_mm = 8.9 mm, soglia 10.0 mm.
- PASS: T3 final target — max tip_target_error_mm = 14.2 mm, soglia 25.0 mm.
- PASS: T3 skull/corridor safety — max skull_violation_mm = 0.0 mm, soglia 0.0 mm.
- PASS: T4 entry RCM stability — max entry_rcm_error_mm = 9.0 mm, soglia 10.0 mm.
- PASS: T4 tip cone tracking — max task4_tip_cone_error_mm = 27.7 mm, soglia 50.0 mm.