# Reporting query performance (live Oracle)

The reporting endpoints run fine on the SQLite CI fixture (tiny data) but several full-scan
`coil` / `ab_job` / `sheet_skid` / `pst_test_result` on the **live** plant Oracle, because their
date/line/customer filters are optional and the no-arg call path applies none. This is the
standing "slow reporting / stacker / transferable" concern (task #7).

Two mitigations: (1) an **application** change already landed — the time-series reports now
default to a bounded window so they always filter; (2) **index recommendations** below, which
require the DBA on shared Oracle (the modern stack is strictly read-only on prod/dev; the app
does not create indexes).

## (1) Application mitigation — default report window (done)

The seven time-series reports resolve a default window (`ResolveReportWindow`, last
`365` days) when `from`/`to` are omitted, so they always emit a date predicate instead of
scanning all history. An explicit bound always wins; only the missing side defaults.

- `GET /reporting/production-summary` — `ab_job.time_date_started`
- `GET /reporting/line-efficiency` — `ab_job.time_date_started` (+ merges downtime)
- `GET /reporting/monthly-production` — `process_coil.process_date`
- `GET /reporting/downtime` — `dt_instance.starting_time`
- `GET /reporting/on-time` — `ab_job.time_date_finished`
- `GET /reporting/customer-shipments` — `shipment.shipment_scheduled_date_time`
- `GET /reporting/qa-mechanical` — `pst_test_result.created_date`

Newer reports are bounded by construction (require a scope): `/reporting/production-order`
(job/order/customer/date) and `/reporting/customer-skid-inventory` (customerId).

## (2) Recommended Oracle indexes (DBA action on `.230` / prod)

The default window only helps if the filtered column is indexed; and the state/inventory
reports below have no date to bound, so indexes are their only lever.

| Report(s) | Query shape | Recommended index |
|---|---|---|
| production-summary, line-efficiency | filter `ab_job.time_date_started`; correlated `SUM(process_coil.process_end_wt)` per job | `ab_job(time_date_started)`, `process_coil(ab_job_num)` |
| on-time | filter `ab_job.time_date_finished`, group by `line_num` | `ab_job(line_num, time_date_finished)` |
| monthly-production | filter `process_coil.process_date` | `process_coil(process_date)` |
| downtime, line-efficiency | filter `dt_instance.starting_time`, by `line_num` | `dt_instance(line_num, starting_time)` |
| qa-mechanical | filter `pst_test_result.created_date` | `pst_test_result(created_date)` |
| customer-shipments, open-shipments | `shipment.shipment_scheduled_date_time`, `date_sent`, `customer_id` | `shipment(customer_id)`, `shipment(date_sent)` |
| **transferable-coils** (known hot spot) | `coil.net_wt_balance > 0`, optional `customer_id`, `LIKE` on org/lot/notes | `coil(customer_id)`, `coil(net_wt_balance)` |
| unmatched-coils | `coil NOT IN (SELECT process_coil)` (full anti-join) | `coil(coil_abc_num)`, `process_coil(coil_abc_num)` |
| coil-inventory, coil-on-hold, coils/summary | `coil` group/scan by `coil_alloy2` / `coil_status` / `coil_location` | `coil(coil_status)`, `coil(customer_id)` |
| skid-inventory, customer-skid-count | `sheet_skid` group; multi-join to customer | `sheet_skid(ab_job_num)`, `sheet_skid(skid_sheet_status)` |
| scrap-summary, scrap-by-job | `scrap_skid` group by type / job | `scrap_skid(scrap_type)`, `scrap_skid(scrap_ab_job_num)` |
| stacker/board | two correlated `COUNT` subqueries per `ab_job` | `process_coil(ab_job_num)`, `sheet_skid(ab_job_num)` |

Most of these likely already exist in prod (they mirror PK/FK columns); this list is the set
to confirm before the reporting surface sees heavy use. Verify against `.230` with the
read-only `oraq` before/after: `SELECT index_name, column_name FROM all_ind_columns WHERE
table_name = 'COIL' ORDER BY index_name, column_position`.

## Not yet addressed (follow-ups)

- The state/inventory list reports (`transferable-coils`, `unmatched-coils`) still return an
  unbounded row set; consider server-side paging if result sizes grow large in practice.
- `line-efficiency` loads all downtime rows for the window and merges in C#; fine within a
  bounded window, revisit if the window is widened materially.
