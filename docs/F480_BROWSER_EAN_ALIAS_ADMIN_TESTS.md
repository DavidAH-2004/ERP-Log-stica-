# F480 - Browser acceptance of EAN alias and gated Admin rotation

- Browser tests use locally launched Vite and intercept every /api request. No Render, Neon, Vercel or real bank connection.
- F411 scanner suite: historical checksum-valid EAN is resolved only with explicit EsAlias and CodigoLeido matching physical scan; UI shows active EAN but preserves original code in POST. Incorrect alias proof returns zero inventory writes.
- e2e-auth-browser: Admin sees the two-admin rotation page but a synthetic 503 keeps it inactive; Vendedor cannot enter the rotation route.
- This tests UI behavior and authorization UX only, not physical barcode scanner, camera or label paper. Hardware gate F398 remains open.