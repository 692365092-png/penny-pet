# Security Policy

## Reporting a vulnerability

Please use GitHub Private Vulnerability Reporting / Security Advisories when available. Do not include passwords, private notes, reminder contents, API tokens, signing keys, or other personal data in a public issue.

## Release integrity

Official release binaries are intended to be built from this public repository by GitHub Actions. The current public workflow produces an unsigned artifact. If signing is added later, credentials must live only in the CI secret store and must never be committed to the repository.

## Future online services

The current release does not call a remote content API. The following rules apply when an online feature is added:

- Never embed a secret third-party API key in the Penny executable, source tree, client settings, cache, or logs. Obfuscation and encrypted constants do not make a client secret safe.
- Services requiring a secret must use `Penny client -> Penny backend -> third-party API`; the secret stays in the backend secret store.
- A token documented as safe for public clients still requires least privilege, rotation, revocation and rate limiting.
- Use HTTPS, explicit timeouts, cancellation, bounded responses and conservative retry behavior. Online feature failure must not stop the pet or local features.
- Validate remote text, image URLs and navigation targets before presentation. Remote data must never become an arbitrary command or local-file operation.
- Ignore compatible unknown JSON fields, validate required fields, and use a new API major version only for breaking protocol changes.

## Logging and sensitive data

Network diagnostics may record the feature/endpoint category, HTTP status, error class, time and client version. They must not record Authorization headers, cookies, API keys, full tokens, URL query secrets, request/response bodies, note text, Todo or Schedule text, reminder content, or keyboard content. Exceptions and URLs must be sanitized before they reach the local diagnostics file.
