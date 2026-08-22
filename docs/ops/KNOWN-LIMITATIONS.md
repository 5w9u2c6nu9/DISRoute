# DISRoute 1.0.0-rc.1 Known Limitations and External Dependencies

- Interactive operational clients are Windows-first. Linux ARM64 support is intentionally limited to Station Display/wallboard use.
- Mobile currently ships a desktop-hosted Avalonia field validation harness and shared Mobile.Core logic. Native Android/iOS packaging, push, MDM, device secure-store and field-device qualification are not part of this RC.
- Windows Service installation scripts and SCM host code are included, but live Windows Server SCM lifecycle acceptance requires Windows Server hardware/VM access.
- The Station Display Linux ARM64 artifact is produced and integrity-checked, but Raspberry Pi display/peripheral/endurance acceptance requires target hardware.
- DISRoute application fencing does not replace database infrastructure fencing. Physical PostgreSQL failover requires an independent application witness and externally managed database fencing/quorum.
- Qualification failover/RPO/RTO measurements are local lab observations and are not contractual production SLAs.
- External provider adapters require vendor-specific conformance testing for the actual agency 911, radio/status, recording/Eventide, NERIS, routing/GIS, ONVIF/camera and AIS systems.
- Code-signing and signed installer acceptance require agency/vendor signing credentials that are not present in the repository or qualification environment.
- The repository does not claim an independent penetration test, WCAG/accessibility certification, or full operator UAT. Those are release-approval inputs outside automated implementation qualification.
