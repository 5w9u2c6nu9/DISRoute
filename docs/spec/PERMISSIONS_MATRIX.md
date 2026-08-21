# Permission and Scope Matrix

This is a starting policy model; agencies customize roles while preserving least privilege and server enforcement.

| Role | Incident Ops | Unit Ops | Supervisor Override | Station Ops | Station Config | Workforce | DMS | Prevention/RMS | Provider/System Admin |
|---|---|---|---|---|---|---|---|---|---|
| Dispatcher | Full assigned CAD scope | Assign/status | No by default | View operational | No | View staffing needed for CAD | Operational docs | Limited | No |
| Call Taker | Intake/create/update | Limited | No | View if needed | No | No/limited | Operational docs | Limited | No |
| Supervisor | Full | Full | Yes by policy | Operational | Limited by role | Manage/approve | Approve/sign as role | Review/correct by role | No |
| Firefighter/Responder | Assigned incidents | Own/assigned unit status | No | Current-station scope | No | Self-service | Assigned docs/tasks | Limited | No |
| Station Officer | Station/assigned incidents | Station resources | Limited operational | Current-station scope | Selected station settings | Station staffing/approvals | Approve/sign as configured | Selected | No |
| Prevention/Inspector | View needed incidents | No/limited | No | View | No | Own schedule | Documents/forms | Full prevention scope | No |
| Records/Reporting | Historical/read/correct by policy | Read | No | Read | No | Limited | Records export/docs | Reporting/RMS | No |
| Workforce Admin | Read operational staffing | No | No | Staffing view | No | Full Workforce | Workforce forms/docs | Personnel operational | No |
| DMS Admin | Read by classification | No | No | No | No | Task integration | Full DMS config | Related docs only | No |
| System Admin | Operational read only if explicitly granted | No by default | No | Device health | Technical config | Module config only | Technical config only | Technical config only | Full platform admin |

## Effective permission

Effective permission is calculated from:

- user/identity;
- base role(s);
- agency/tenant;
- station/resource scope;
- current duty assignment;
- temporary detail/acting role;
- explicit temporary grant;
- module enablement;
- data classification/need-to-know;
- device trust/capability;
- effective start/end time.

Temporary assignment changes should push to active sessions in realtime without requiring logout/login.
