# F472 — Release seguro de alias y rotación EAN

Status: implemented on \`development\`, **Production NO-GO** until #65/#70 authorization.

- \`BarcodeRotationDeploymentGuard.Validate\` runs before the server opens HTTP or database connections. \`Barcodes:EnableEanRotation=true\` requires \`Barcodes:EnableHistoricalAliasLookup=true\`; invalid boolean strings reject startup instead of silently becoming OFF.
- On the current **Render Production** identity \`marketplaceerp-api\`, rotations are deliberately rejected regardless of flags. A future explicit reviewed change is required to lift this protection after F398 physical hardware, F470 2-admin SQL and data recovery acceptance. Normal service with both flags unset remains unaffected.
- \`RotacionesEanController\` is Admin-only; when inactive it returns 503 without database writes. No direct user-supplied approver identity is accepted: the authenticated account claim is used.
- \`FinanceServiceExtensions\` in Production requires \`Payments:Provider=Disabled\`; it neither registers \`IPaymentRecoveryGateway\` nor permits a Mock gateway in Production. F472 unit tests assert these constraints.
- This fail-closed code is only a prerequisite; it **does not certify** the effective Production connection, backup, Vercel, TLS ingress identity or an approved migration. Main and PROD must not be promoted on this evidence alone.
