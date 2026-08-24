# Security policy

Please report suspected vulnerabilities privately to `security@pritset.com`. Do not include access tokens, secrets, customer documents, or production payloads in a public issue.

Only the latest released minor version receives security fixes during the pre-1.0 period. Rotate any credential that may have appeared in logs, error dumps, shell history, or source control.

The SDK requires HTTPS except for exact loopback development endpoints. Its default HTTP handler disables redirects. Injected `HttpClient` instances require explicit confirmation that their redirect policy is safe. Credential-bearing transport exceptions are sanitized, uploads remain stream-based, and retained API error bodies are capped.
