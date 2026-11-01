# Security Policy

## Supported versions

Security reports are currently accepted for the latest development line on the `main` branch.

## Reporting a vulnerability

Please do not open a public GitHub issue for an exploitable vulnerability. Use GitHub's **Report a vulnerability** / private vulnerability reporting feature if it is enabled for this repository. Include:

- A clear description and impact.
- Reproduction steps or a proof of concept.
- Affected version/commit and relevant environment details.

Do not include real credentials, personal data, or production customer data in a report.

## Deployment security

- Never commit `.env`, production connection strings, signing keys, certificates, access tokens, or real customer data.
- Configure `Authentication__JwtSigningKey`, `Bootstrap__Key`, and database credentials through a secret manager or deployment environment.
- Use a unique, high-entropy JWT signing key (at least 32 bytes) and rotate it according to your operational policy.
- Run the API with the restricted application database role; do not use the PostgreSQL superuser or schema owner.
- Review tenant isolation, PostgreSQL RLS policies, migrations, backups, logging, and authorization before production deployment.
