# DISRoute Workforce

DISRoute Workforce is a public-safety scheduling, staffing, time-management and employee self-service module. It is not a full payroll/benefits/tax HRIS.

## Scheduling

Support:

- recurring shift rotations (24/48, 48/96, Kelly-style, custom patterns);
- master schedule;
- station/unit staffing;
- minimum staffing rules;
- rank/qualification requirements;
- open shifts;
- overtime lists/opportunities;
- call-outs;
- sick/leave coverage;
- vacation/PTO requests;
- shift trades/swaps;
- temporary station details;
- acting assignments;
- training assignments;
- intra-day crew changes/relief;
- timesheets/time entries;
- leave balances/projections when configured;
- real-time notifications;
- mobile/web self-service;
- supervisor approval/audit;
- payroll-ready export.

## Staffing rules

Rules may consider station/unit, minimum headcount, officer/driver/operator qualification, medical/marine/specialty qualification, overtime/seniority/order policy, rest/hours, exclusions, and agency/collective-bargaining rules configured by the agency.

DISRoute recommends/flags deficiencies; it should not autonomously assign personnel unless policy explicitly enables it.

## Duty assignments and permissions

Home station, current duty station, unit, acting rank, temporary detail, radio assignment and security scope are separate effective-time concepts.

A temporary detail can automatically grant station-scoped operational access for the assignment and revoke it at end/relief without changing the person's permanent base role. Home-station residual access is agency policy.

## Relief

Crew/person relief should be atomic at an effective time so accountability does not show artificial gaps. Support whole-crew relief and individual replacement.

## Daily printed schedule

Generate agency-branded PDF/print views:

- Current Schedule — effective staffing as of a timestamp.
- Current Schedule + Changes — effective roster plus same-day change history.

Changes may include details, swaps, call-outs, overtime, acting assignments, training, reliefs, radio assignments if selected, who entered the change, and when. The original posted schedule is preserved; print is a point-in-time snapshot.

## Timesheets/payroll

DISRoute may calculate categorized time totals and route timesheet verification/approval, but payroll calculations, taxes, benefits, deductions, etc. remain external. Export through validated provider interfaces.
